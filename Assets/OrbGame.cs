using UnityEngine;
using System.Collections.Generic;

// Moves and pulses each orb
public class OrbData : MonoBehaviour
{
    public int type;   // 0 normal, 1 gold, 2 bomb, 3 ice, 4 blast, 5 double
    public float size;
    public float speedX;
    public float baseY;
    public float bobSpeed;
    public float phase;
    float t;

    void Update()
    {
        float k = OrbGame.SpeedMult;
        Vector3 p = transform.position;
        p.x += speedX * k * Time.deltaTime;
        if (p.x > 6f || p.x < -6f) speedX = -speedX;
        t += Time.deltaTime * k;
        p.y = baseY + Mathf.Sin(t * bobSpeed + phase) * 0.5f;
        transform.position = p;

        // gentle pulse
        float pulse = 1f + Mathf.Sin(Time.time * 5f + phase) * 0.06f;
        transform.localScale = Vector3.one * size * pulse;
    }
}

public class Shard
{
    public GameObject go;
    public Vector3 vel;
    public float life;
}

public class FloatText
{
    public string text;
    public Vector3 pos;
    public float life;
    public Color color;
}

public class OrbGame : MonoBehaviour
{
    public static float SpeedMult = 1f;

    enum State { Menu, Playing, GameOver }
    State state = State.Menu;

    // Difficulty: 0 Easy, 1 Medium, 2 Hard
    int diff = 1;
    float[] diffSpeed = { 0.7f, 1f, 1.4f };
    float[] diffBomb = { 0.08f, 0.13f, 0.20f };
    float[] diffTime = { 40f, 30f, 25f };
    string[] diffName = { "EASY", "MEDIUM", "HARD" };

    int score, bestScore, combo;
    int clicks, hits;
    bool newBest, paused;
    float timeLeft, timer;
    float iceTimer, doubleTimer;
    float shakeTime, flashAlpha;
    Vector3 camBase;

    List<GameObject> orbs = new List<GameObject>();
    List<Shard> shards = new List<Shard>();
    List<FloatText> texts = new List<FloatText>();

    AudioSource audioSrc;
    AudioClip popClip, goldClip, bombClip, powerClip;

    void Start()
    {
        Camera cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.03f, 0.1f);
        camBase = cam.transform.position;

        audioSrc = gameObject.AddComponent<AudioSource>();
        popClip = MakeTone(600f, 0.12f);
        goldClip = MakeTone(1000f, 0.3f);
        bombClip = MakeTone(120f, 0.4f);
        powerClip = MakeTone(800f, 0.35f);

        BuildGrid();
    }

    // ---------- Sound made from code ----------
    AudioClip MakeTone(float freq, float duration)
    {
        int rate = 44100;
        int n = (int)(rate * duration);
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float fade = 1f - (float)i / n;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * fade * 0.4f;
        }
        AudioClip clip = AudioClip.Create("tone", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------- Grid ----------
    void BuildGrid()
    {
        GameObject gridParent = new GameObject("Grid");
        Color lineColor = new Color(0.1f, 0.5f, 1f);

        for (float x = -12; x <= 12; x += 1f)
            MakeLine(gridParent, new Vector3(x, 1, 2), new Vector3(0.03f, 14f, 0.03f), lineColor);

        for (float y = -6; y <= 8; y += 1f)
            MakeLine(gridParent, new Vector3(0, y, 2), new Vector3(24f, 0.03f, 0.03f), lineColor);
    }

    void MakeLine(GameObject parent, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = "GridLine";
        line.transform.parent = parent.transform;
        line.transform.position = pos;
        line.transform.localScale = scale;
        line.GetComponent<Renderer>().material.color = color;
        Destroy(line.GetComponent<Collider>());
    }

    // ---------- Game flow ----------
    void StartGame(int difficulty)
    {
        diff = difficulty;
        ClearOrbs();
        bestScore = PlayerPrefs.GetInt("Best" + diff, 0);
        score = 0;
        combo = 0;
        clicks = 0;
        hits = 0;
        timeLeft = diffTime[diff];
        timer = 0;
        iceTimer = 0;
        doubleTimer = 0;
        newBest = false;
        paused = false;
        Time.timeScale = 1f;
        state = State.Playing;
    }

    void EndGame()
    {
        ClearOrbs();
        Time.timeScale = 1f;
        SpeedMult = 1f;
        state = State.GameOver;

        if (score > bestScore)
        {
            bestScore = score;
            newBest = true;
            PlayerPrefs.SetInt("Best" + diff, bestScore);
            PlayerPrefs.Save();
        }
    }

    void ClearOrbs()
    {
        foreach (GameObject o in orbs)
            if (o != null) Destroy(o);
        orbs.Clear();
    }

    int Multiplier()
    {
        return Mathf.Min(1 + combo / 3, 5);
    }

    int AddScore(int pts)
    {
        if (doubleTimer > 0) pts *= 2;
        score += pts;
        return pts;
    }

    void Update()
    {
        UpdateShards();
        UpdateTexts();

        if (flashAlpha > 0) flashAlpha -= Time.unscaledDeltaTime * 2f;

        // Pause
        if (state == State.Playing && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)))
        {
            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;
        }

        if (state != State.Playing || paused) return;

        // Power-up timers
        if (iceTimer > 0) iceTimer -= Time.deltaTime;
        if (doubleTimer > 0) doubleTimer -= Time.deltaTime;
        SpeedMult = (iceTimer > 0 ? 0.4f : 1f) * diffSpeed[diff];

        orbs.RemoveAll(o => o == null);

        timeLeft -= Time.deltaTime;
        timer += Time.deltaTime;

        // Orbs spawn faster as time runs out
        float progress = Mathf.Clamp01(1f - timeLeft / diffTime[diff]);
        float spawnInterval = Mathf.Lerp(0.8f, 0.35f, progress);

        if (timer >= spawnInterval)
        {
            timer = 0;
            SpawnOrb();
        }

        if (timeLeft <= 0)
        {
            EndGame();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            clicks++;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            bool hitOrb = false;

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.name == "Orb")
                {
                    hitOrb = true;
                    hits++;
                    HitOrb(hit.collider.gameObject);
                }
            }

            if (!hitOrb) combo = 0;
        }
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (shakeTime > 0)
        {
            shakeTime -= Time.unscaledDeltaTime;
            cam.transform.position = camBase + (Vector3)Random.insideUnitCircle * 0.15f;
        }
        else
        {
            cam.transform.position = camBase;
        }
    }

    void HitOrb(GameObject orb)
    {
        OrbData data = orb.GetComponent<OrbData>();
        Vector3 pos = orb.transform.position;
        Color color = orb.GetComponent<Renderer>().material.color;

        orbs.Remove(orb);
        Destroy(orb);

        if (data.type == 2) // BOMB
        {
            score = Mathf.Max(0, score - 5);
            combo = 0;
            audioSrc.pitch = 1f;
            audioSrc.PlayOneShot(bombClip);
            SpawnShards(pos, Color.red, 14);
            AddText("-5", pos, Color.red);
            shakeTime = 0.3f;
            flashAlpha = 0.5f;
        }
        else if (data.type == 1) // GOLD
        {
            combo++;
            int p = AddScore(5 * Multiplier());
            timeLeft += 3f;
            audioSrc.pitch = 1f;
            audioSrc.PlayOneShot(goldClip);
            SpawnShards(pos, Color.yellow, 14);
            AddText("+" + p + "  +3s", pos, Color.yellow);
        }
        else if (data.type == 3) // ICE
        {
            combo++;
            iceTimer = 5f;
            audioSrc.pitch = 1f;
            audioSrc.PlayOneShot(powerClip);
            SpawnShards(pos, Color.cyan, 14);
            AddText("SLOW MOTION!", pos, Color.cyan);
        }
        else if (data.type == 4) // BLAST
        {
            combo++;
            audioSrc.pitch = 1f;
            audioSrc.PlayOneShot(powerClip);
            AddText("BLAST!", pos, new Color(0.8f, 0.4f, 1f));
            Blast();
        }
        else if (data.type == 5) // DOUBLE
        {
            combo++;
            doubleTimer = 10f;
            audioSrc.pitch = 1f;
            audioSrc.PlayOneShot(powerClip);
            SpawnShards(pos, Color.green, 14);
            AddText("DOUBLE POINTS!", pos, Color.green);
        }
        else // NORMAL
        {
            combo++;
            int points = 1;
            if (data.size < 0.6f) points = 3;
            else if (data.size < 1.1f) points = 2;
            int p = AddScore(points * Multiplier());

            audioSrc.pitch = 1f + Mathf.Min(combo, 12) * 0.06f;
            audioSrc.PlayOneShot(popClip);
            SpawnShards(pos, color, 10);
            AddText("+" + p, pos, Color.white);
        }
    }

    // Pops every orb on screen
    void Blast()
    {
        foreach (GameObject o in new List<GameObject>(orbs))
        {
            if (o == null) continue;
            OrbData d = o.GetComponent<OrbData>();
            Vector3 pos = o.transform.position;
            Color c = o.GetComponent<Renderer>().material.color;

            if (d.type != 2)
            {
                int p = AddScore(2);
                AddText("+" + p, pos, Color.white);
            }
            SpawnShards(pos, c, 8);
            Destroy(o);
        }
        orbs.Clear();
        shakeTime = 0.2f;
    }

    void SpawnOrb()
    {
        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orb.name = "Orb";

        float size = Random.Range(0.4f, 1.5f);
        float startY = Random.Range(0.8f, 3.5f);
        orb.transform.localScale = Vector3.one * size;
        orb.transform.position = new Vector3(Random.Range(-5f, 5f), startY, 0);

        OrbData data = orb.AddComponent<OrbData>();
        data.size = size;
        data.baseY = startY;

        float dir = Random.value < 0.5f ? -1f : 1f;
        data.speedX = dir * Random.Range(0.8f, 1.8f);
        data.bobSpeed = Random.Range(1f, 2.2f);
        data.phase = Random.Range(0f, 6f);

        // Decide the orb type
        float roll = Random.value;
        float bomb = diffBomb[diff];
        Color color;

        if (roll < 0.09f)                         // gold
        {
            data.type = 1;
            color = Color.yellow;
        }
        else if (roll < 0.09f + bomb)             // bomb
        {
            data.type = 2;
            color = new Color(0.5f, 0f, 0f);
        }
        else if (roll < 0.09f + bomb + 0.04f)     // ice
        {
            data.type = 3;
            color = Color.cyan;
        }
        else if (roll < 0.09f + bomb + 0.07f)     // blast
        {
            data.type = 4;
            color = new Color(0.7f, 0.2f, 1f);
        }
        else if (roll < 0.09f + bomb + 0.11f)     // double
        {
            data.type = 5;
            color = Color.green;
        }
        else                                      // normal
        {
            data.type = 0;
            color = Random.ColorHSV(0, 1, 0.7f, 1, 1, 1);
        }

        orb.GetComponent<Renderer>().material.color = color;

        orbs.Add(orb);
        Destroy(orb, 4f);
    }

    // ---------- Pop effect ----------
    void SpawnShards(Vector3 pos, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Shard";
            Destroy(s.GetComponent<Collider>());
            s.transform.position = pos;
            s.transform.localScale = Vector3.one * 0.15f;
            s.GetComponent<Renderer>().material.color = color;

            Shard sh = new Shard();
            sh.go = s;
            sh.vel = Random.insideUnitSphere * 4f;
            sh.life = 0.6f;
            shards.Add(sh);
        }
    }

    void UpdateShards()
    {
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            Shard s = shards[i];
            s.life -= Time.deltaTime;

            if (s.life <= 0 || s.go == null)
            {
                if (s.go != null) Destroy(s.go);
                shards.RemoveAt(i);
                continue;
            }

            s.vel.y -= 6f * Time.deltaTime;
            s.go.transform.position += s.vel * Time.deltaTime;
            s.go.transform.localScale = Vector3.one * 0.15f * (s.life / 0.6f);
        }
    }

    // ---------- Floating text ----------
    void AddText(string t, Vector3 pos, Color c)
    {
        FloatText f = new FloatText();
        f.text = t;
        f.pos = pos;
        f.color = c;
        f.life = 0.9f;
        texts.Add(f);
    }

    void UpdateTexts()
    {
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            texts[i].life -= Time.deltaTime;
            texts[i].pos += Vector3.up * 1.2f * Time.deltaTime;
            if (texts[i].life <= 0) texts.RemoveAt(i);
        }
    }

    // ---------- Blocky pixel font ----------
    static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        {'O', new[]{"01110","10001","10001","10001","10001","10001","01110"}},
        {'R', new[]{"11110","10001","10001","11110","10100","10010","10001"}},
        {'B', new[]{"11110","10001","10001","11110","10001","10001","11110"}},
        {'P', new[]{"11110","10001","10001","11110","10000","10000","10000"}},
        {'G', new[]{"01110","10001","10000","10111","10001","10001","01110"}},
        {'A', new[]{"01110","10001","10001","11111","10001","10001","10001"}},
        {'M', new[]{"10001","11011","10101","10101","10001","10001","10001"}},
        {'E', new[]{"11111","10000","10000","11110","10000","10000","11111"}},
        {'V', new[]{"10001","10001","10001","10001","10001","01010","00100"}},
        {' ', new[]{"00000","00000","00000","00000","00000","00000","00000"}},
    };

    void DrawPixelText(string str, Rect area, Color color)
    {
        int n = str.Length;
        float cols = n * 6 - 1;                       // 5 columns per letter + 1 gap
        float cell = Mathf.Min(area.width / cols, area.height / 7f);
        float startX = area.x + (area.width - cols * cell) / 2f;
        float startY = area.y + (area.height - 7f * cell) / 2f;
        float gap = cell * 0.08f;                     // thin gaps make it look like blocks

        Color old = GUI.color;
        for (int pass = 0; pass < 2; pass++)          // pass 0 = shadow, pass 1 = letters
        {
            for (int i = 0; i < n; i++)
            {
                if (!Font.ContainsKey(str[i])) continue;
                string[] rows = Font[str[i]];
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                    {
                        if (rows[r][c] != '1') continue;
                        float x = startX + (i * 6 + c) * cell;
                        float y = startY + r * cell;

                        if (pass == 0)
                        {
                            GUI.color = new Color(0f, 0f, 0f, 0.6f);
                            GUI.DrawTexture(new Rect(x + cell * 0.18f, y + cell * 0.18f, cell - gap, cell - gap), Texture2D.whiteTexture);
                        }
                        else
                        {
                            GUI.color = color;
                            GUI.DrawTexture(new Rect(x, y, cell - gap, cell - gap), Texture2D.whiteTexture);
                            // light highlight on the top-left of each block
                            GUI.color = new Color(1f, 1f, 1f, 0.35f);
                            GUI.DrawTexture(new Rect(x, y, (cell - gap) * 0.45f, (cell - gap) * 0.45f), Texture2D.whiteTexture);
                        }
                    }
            }
        }
        GUI.color = old;
    }

    // ---------- Screen text and buttons ----------
    void OnGUI()
    {
        int big = Screen.height / 10;
        int mid = Screen.height / 20;
        int small = Screen.height / 36;

        GUIStyle title = new GUIStyle(GUI.skin.label);
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;
        title.fontSize = big;
        title.normal.textColor = Color.white;

        GUIStyle text = new GUIStyle(title);
        text.fontSize = mid;
        text.fontStyle = FontStyle.Normal;

        GUIStyle hint = new GUIStyle(text);
        hint.fontSize = small;
        hint.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        GUIStyle button = new GUIStyle(GUI.skin.button);
        button.fontSize = mid;
        button.fontStyle = FontStyle.Bold;

        // Red flash when a bomb is hit
        if (flashAlpha > 0)
        {
            GUI.color = new Color(1f, 0f, 0f, flashAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        if (state == State.Playing)
        {
            // Floating texts
            GUIStyle ft = new GUIStyle(text);
            ft.fontStyle = FontStyle.Bold;
            ft.fontSize = mid;
            foreach (FloatText f in texts)
            {
                Vector3 sp = Camera.main.WorldToScreenPoint(f.pos);
                ft.normal.textColor = new Color(f.color.r, f.color.g, f.color.b, Mathf.Clamp01(f.life * 2f));
                GUI.Label(new Rect(sp.x - 200, Screen.height - sp.y - mid, 400, mid * 2), f.text, ft);
            }

            // HUD
            GUIStyle hud = new GUIStyle(text);
            hud.alignment = TextAnchor.MiddleLeft;

            GUI.Label(new Rect(20, 10, 900, mid * 2),
                "Score: " + score + "    Time: " + Mathf.CeilToInt(timeLeft), hud);

            float line = 10 + mid * 1.6f;

            if (combo >= 3)
            {
                hud.normal.textColor = Color.yellow;
                GUI.Label(new Rect(20, line, 900, mid * 2),
                    "COMBO x" + Multiplier() + "  (" + combo + " in a row)", hud);
                line += mid * 1.4f;
            }
            if (iceTimer > 0)
            {
                hud.normal.textColor = Color.cyan;
                GUI.Label(new Rect(20, line, 900, mid * 2), "SLOW  " + Mathf.CeilToInt(iceTimer) + "s", hud);
                line += mid * 1.4f;
            }
            if (doubleTimer > 0)
            {
                hud.normal.textColor = Color.green;
                GUI.Label(new Rect(20, line, 900, mid * 2), "DOUBLE POINTS  " + Mathf.CeilToInt(doubleTimer) + "s", hud);
            }

            hint.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(Screen.width - 420, 10, 400, small * 2), "Esc = pause", hint);

            if (paused)
            {
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, big * 1.5f), "PAUSED", title);
                GUI.Label(new Rect(0, Screen.height * 0.5f, Screen.width, mid * 2), "Press Esc or P to continue", text);
            }
            return;
        }

        float w = Screen.width * 0.6f;
        float h = Screen.height * 0.82f;
        Rect panel = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        float cx = panel.x;
        float y = panel.y;

        GUI.Box(panel, "");

        if (state == State.Menu)
        {
            DrawPixelText("ORB POP", new Rect(cx + w * 0.1f, y + h * 0.03f, w * 0.8f, h * 0.15f), new Color(1f, 0.8f, 0.1f));

            string[] lines =
            {
                "Gold orb = +3 seconds",
                "Dark red bomb = lose points. Avoid!",
                "Cyan orb = slow motion",
                "Purple orb = pops everything",
                "Green orb = double points",
                "Small orbs = more points. Build combos!"
            };
            Color[] cols =
            {
                Color.yellow, new Color(1f, 0.4f, 0.4f), Color.cyan,
                new Color(0.8f, 0.5f, 1f), Color.green, new Color(0.8f, 0.8f, 0.8f)
            };

            hint.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < lines.Length; i++)
            {
                hint.normal.textColor = cols[i];
                GUI.Label(new Rect(cx, y + h * (0.20f + i * 0.075f), w, h * 0.07f), lines[i], hint);
            }

            hint.normal.textColor = Color.white;
            GUI.Label(new Rect(cx, y + h * 0.66f, w, h * 0.07f), "Choose a difficulty to start:", hint);

            float bw = w * 0.28f;
            for (int i = 0; i < 3; i++)
            {
                float bx = cx + w * 0.04f + i * (bw + w * 0.04f);
                string label = diffName[i] + "\nBest: " + PlayerPrefs.GetInt("Best" + i, 0);
                if (GUI.Button(new Rect(bx, y + h * 0.75f, bw, h * 0.18f), label, button))
                    StartGame(i);
            }
        }
        else if (state == State.GameOver)
        {
            DrawPixelText("GAME OVER", new Rect(cx + w * 0.08f, y + h * 0.05f, w * 0.84f, h * 0.15f), new Color(1f, 0.3f, 0.3f));

            hint.alignment = TextAnchor.MiddleCenter;
            hint.normal.textColor = Color.white;
            GUI.Label(new Rect(cx, y + h * 0.22f, w, h * 0.07f), "Difficulty: " + diffName[diff], hint);

            int acc = clicks > 0 ? Mathf.RoundToInt(100f * hits / clicks) : 0;
            GUI.Label(new Rect(cx, y + h * 0.31f, w, h * 0.1f), "Score: " + score, text);
            GUI.Label(new Rect(cx, y + h * 0.42f, w, h * 0.1f), "Best Score: " + bestScore, text);
            GUI.Label(new Rect(cx, y + h * 0.53f, w, h * 0.1f), "Accuracy: " + acc + "%", text);

            if (newBest)
            {
                text.normal.textColor = Color.yellow;
                GUI.Label(new Rect(cx, y + h * 0.64f, w, h * 0.1f), "NEW BEST!", text);
            }

            if (GUI.Button(new Rect(cx + w * 0.05f, y + h * 0.78f, w * 0.43f, h * 0.15f), "PLAY AGAIN", button))
                StartGame(diff);
            if (GUI.Button(new Rect(cx + w * 0.52f, y + h * 0.78f, w * 0.43f, h * 0.15f), "MENU", button))
                state = State.Menu;
        }
    }
}