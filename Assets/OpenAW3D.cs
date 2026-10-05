using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OpenAW3DCell : MonoBehaviour { public int x, y; }

// A self-contained reconstruction driven only by recreation_information.
public sealed class OpenAW3D : MonoBehaviour
{
    const int Width = 14, Height = 12, MoveRange = 7, TankPrice = 6000;
    enum ScreenState { Title, Tutorial, Play, Win }
    enum CommandState { None, Move, Actions, Aim, Buy, Options }
    enum Ground { Grass, Road, Water, Bridge }
    enum Structure { City, Base }

    sealed class Building
    {
        public int x, y, owner, capture = 20;
        public Structure type;
        public GameObject visual;
    }
    sealed class Tank
    {
        public int x, y, owner, hp = 10;
        public bool acted;
        public GameObject visual;
    }

    readonly Ground[,] ground = new Ground[Width, Height];
    readonly GameObject[,] tiles = new GameObject[Width, Height];
    readonly Building[,] buildings = new Building[Width, Height];
    readonly List<Tank> tanks = new List<Tank>();
    readonly Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
    readonly HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
    readonly List<GameObject> effects = new List<GameObject>();
    readonly int[] funds = { 8000, 8000 };
    ScreenState screen = ScreenState.Title;
    CommandState command;
    Tank selected;
    int current, day = 1, winner = -1, originX, originY, hoverX = -1, hoverY = -1;
    bool moved;
    bool movingAnimation;
    LineRenderer routeLine;
    Camera boardCamera;
    Shader flatShader;
    Transform boardRoot;
    Vector3 cameraFocus = new Vector3(6.5f, 0, 5.5f);
    float cameraDistance = 20f;
    float bannerUntil;
    GUIStyle heading, body, small, button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<OpenAW3D>() == null)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                Destroy(root);
            var game = new GameObject("OpenAW3D Game").AddComponent<OpenAW3D>();
            if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-openaw3d-smoke" || arg == "-openaw3d-shot" || arg == "-openaw3d-rules"))
            {
                game.StartMatch();
                if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-openaw3d-shot")) game.StartCoroutine(game.CapturePreview());
            }
        }
    }

    void Awake()
    {
        Application.targetFrameRate = 60;
        flatShader = Resources.Load<Shader>("OpenAW3D/Flat");
        SetupCamera();
    }

    void SetupCamera()
    {
        var cameraObject = new GameObject("Strategy Camera");
        boardCamera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<AudioListener>();
        boardCamera.clearFlags = CameraClearFlags.SolidColor;
        boardCamera.backgroundColor = new Color(0.08f, 0.14f, 0.18f);
        boardCamera.fieldOfView = 52f;
        MoveCamera();
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.25f;
        sun.transform.rotation = Quaternion.Euler(48, -35, 0);
    }

    void MoveCamera()
    {
        boardCamera.transform.position = cameraFocus + new Vector3(0, cameraDistance * .95f, cameraDistance * .55f);
        boardCamera.transform.LookAt(cameraFocus);
    }

    void SetupStyles()
    {
        heading = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 37, fontStyle = FontStyle.Bold };
        heading.normal.textColor = Color.white;
        body = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 19, wordWrap = true };
        body.normal.textColor = Color.white;
        small = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
        small.normal.textColor = Color.white;
        button = new GUIStyle(GUI.skin.button) { fontSize = 19, fontStyle = FontStyle.Bold };
    }

    void StartMatch()
    {
        if (boardRoot != null) Destroy(boardRoot.gameObject);
        boardRoot = new GameObject("Battlefield").transform;
        tanks.Clear();
        effects.Clear();
        Array.Clear(buildings, 0, buildings.Length);
        funds[0] = funds[1] = 8000;
        current = 0; day = 1; winner = -1; selected = null; command = CommandState.None; moved = false; movingAnimation = false;
        cameraFocus = new Vector3(6.5f, 0, 5.5f); cameraDistance = 19f; MoveCamera();
        GenerateGround();
        var routeObject = new GameObject("Route preview");
        routeObject.transform.SetParent(boardRoot);
        routeLine = routeObject.AddComponent<LineRenderer>();
        routeLine.material = NewMaterial();
        routeLine.startColor = routeLine.endColor = new Color(1, .9f, .2f);
        routeLine.startWidth = routeLine.endWidth = .12f;
        routeLine.positionCount = 0;
        MakeBuilding(1, 10, 0, Structure.Base);
        MakeBuilding(12, 2, 1, Structure.Base);
        MakeBuilding(4, 8, -1, Structure.City);
        MakeBuilding(5, 6, -1, Structure.City);
        MakeBuilding(8, 5, -1, Structure.City);
        MakeBuilding(10, 2, 1, Structure.City);
        MakeTank(2, 11, 0, false);
        MakeTank(10, 1, 1, false);
        screen = ScreenState.Tutorial;
        bannerUntil = Time.time + 2f;
        RefreshVisuals();
        Debug.Log("OpenAW3D match ready: " + tanks.Count + " tanks, day " + day);
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-openaw3d-rules")) RunRuleChecks();
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-openaw3d-smoke"))
        {
            BuildReachable(tanks[0]);
            Debug.Log("Replay red opening reachable: " + reachable.Contains(new Vector2Int(4, 7)));
            BuildReachable(tanks[1]);
            Debug.Log("Replay blue opening reachable: " + reachable.Contains(new Vector2Int(8, 5)));
            reachable.Clear();
        }
    }

    IEnumerator CapturePreview()
    {
        screen = ScreenState.Play;
        for (int i = 0; i < 10; i++) yield return null;
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows", "qa.png");
        var target = new RenderTexture(800, 600, 24);
        boardCamera.targetTexture = target;
        boardCamera.Render();
        RenderTexture.active = target;
        var pixels = new Texture2D(800, 600, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 800, 600), 0, 0);
        pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        RenderTexture.active = null;
        boardCamera.targetTexture = null;
        Destroy(pixels); Destroy(target);
        Debug.Log("OpenAW3D preview saved: " + path);
        yield return new WaitForSeconds(2);
        Application.Quit();
    }

    void RunRuleChecks()
    {
        var red = tanks[0]; var blue = tanks[1];
        originX = 1; originY = 10; Buy();
        if (funds[0] != 2000 || UnitAt(1, 10) == null || !UnitAt(1, 10).acted)
            Debug.LogError("OpenAW3D purchase rule failed");
        EndTurn();
        if (current != 1 || funds[0] != 5000) Debug.LogError("OpenAW3D income rule failed");
        current = 0;
        red.x = 4; red.y = 8; selected = red; Capture();
        if (buildings[4, 8].capture != 10) Debug.LogError("OpenAW3D capture progress failed");
        selected = red; Capture();
        if (buildings[4, 8].owner != 0) Debug.LogError("OpenAW3D capture transfer failed");
        blue.x = 5; blue.y = 8; selected = red; Attack(red, blue);
        if (blue.hp != 5 || red.hp != 9) Debug.LogError("OpenAW3D combat rule failed");
        foreach (var building in buildings) if (building != null && building.owner == 1) building.owner = -1;
        DestroyTank(blue); CheckWinner();
        if (winner != 0) Debug.LogError("OpenAW3D victory rule failed");
        else Debug.Log("OpenAW3D rule checks passed");
    }

    void GenerateGround()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Ground kind = Ground.Grass;
            if (x == 6 || x == 7) kind = Ground.Water;
            if ((x == 6 || x == 7) && y == 5) kind = Ground.Bridge;
            if ((x == 6 || x == 7) && y == 10) kind = Ground.Bridge;
            if (kind == Ground.Grass && (y == 5 || y == 10 || x == 2 || x == 10)) kind = Ground.Road;
            ground[x, y] = kind;
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "Tile " + x + "," + y;
            tile.transform.SetParent(boardRoot);
            tile.transform.position = new Vector3(x, kind == Ground.Water ? -.25f : 0, y);
            tile.transform.localScale = new Vector3(.98f, kind == Ground.Water ? .16f : .3f, .98f);
            var cell = tile.AddComponent<OpenAW3DCell>(); cell.x = x; cell.y = y;
            tile.GetComponent<Renderer>().material = NewMaterial();
            string textureName = kind == Ground.Water ? "water" : kind == Ground.Bridge ? "bridge" : kind == Ground.Road ? "road" : "grass";
            var texture = Resources.Load<Texture2D>("OpenAW3D/" + textureName);
            if (texture != null) tile.GetComponent<Renderer>().material.mainTexture = texture;
            tiles[x, y] = tile;
        }
    }

    void MakeBuilding(int x, int y, int owner, Structure type)
    {
        var item = new Building { x = x, y = y, owner = owner, type = type };
        buildings[x, y] = item;
        item.visual = new GameObject(type + " " + x + "," + y);
        item.visual.transform.SetParent(boardRoot);
        item.visual.transform.position = new Vector3(x, .24f, y);
        var baseShape = Shape(PrimitiveType.Cube, item.visual.transform, new Vector3(0, .12f, 0), new Vector3(.75f, .25f, .75f));
        var roof = Shape(type == Structure.Base ? PrimitiveType.Cylinder : PrimitiveType.Cube, item.visual.transform, new Vector3(0, .43f, 0), new Vector3(type == Structure.Base ? .6f : .48f, .25f, type == Structure.Base ? .6f : .48f));
        RemoveCollider(baseShape); RemoveCollider(roof);
    }

    Tank MakeTank(int x, int y, int owner, bool acted)
    {
        var unit = new Tank { x = x, y = y, owner = owner, acted = acted };
        unit.visual = new GameObject("Tank " + (owner == 0 ? "Red" : "Blue"));
        unit.visual.transform.SetParent(boardRoot);
        unit.visual.transform.position = new Vector3(x, .38f, y);
        var hull = Shape(PrimitiveType.Cube, unit.visual.transform, Vector3.zero, new Vector3(.72f, .25f, .68f));
        var turret = Shape(PrimitiveType.Cylinder, unit.visual.transform, new Vector3(0, .22f, 0), new Vector3(.36f, .18f, .36f));
        var barrel = Shape(PrimitiveType.Cube, unit.visual.transform, new Vector3(0, .24f, .37f), new Vector3(.12f, .1f, .55f));
        RemoveCollider(hull); RemoveCollider(turret); RemoveCollider(barrel);
        var click = unit.visual.AddComponent<BoxCollider>(); click.size = new Vector3(.83f, .9f, .83f); click.center = new Vector3(0, .18f, 0);
        var cell = unit.visual.AddComponent<OpenAW3DCell>(); cell.x = x; cell.y = y;
        tanks.Add(unit);
        return unit;
    }

    Material NewMaterial() { return new Material(flatShader); }

    GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        var shape = GameObject.CreatePrimitive(type);
        shape.transform.SetParent(parent);
        shape.transform.localPosition = localPosition;
        shape.transform.localScale = localScale;
        shape.GetComponent<Renderer>().material = NewMaterial();
        return shape;
    }

    static void RemoveCollider(GameObject shape)
    {
        var collider = shape.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
    }

    void Update()
    {
        if (!Application.isFocused) return;
        if (Input.GetMouseButtonDown(1) && !movingAnimation) Cancel();
        if (screen != ScreenState.Play) return;
        float horizontal = Input.GetAxisRaw("Horizontal"), vertical = Input.GetAxisRaw("Vertical");
        var mouse = Input.mousePosition;
        bool mouseInside = mouse.x >= 0 && mouse.x < Screen.width && mouse.y >= 0 && mouse.y < Screen.height;
        if (mouseInside)
        {
            if (mouse.x < 12) horizontal -= 1;
            if (mouse.x > Screen.width - 12) horizontal += 1;
            if (mouse.y < 12) vertical -= 1;
            if (mouse.y > Screen.height - 12) vertical += 1;
        }
        cameraFocus += new Vector3(horizontal, 0, vertical) * Time.deltaTime * 8;
        cameraFocus.x = Mathf.Clamp(cameraFocus.x, 0, Width - 1);
        cameraFocus.z = Mathf.Clamp(cameraFocus.z, 0, Height - 1);
        cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * 2, 9, 30);
        MoveCamera();
        int x, y;
        if (MouseCell(out x, out y))
        {
            if (x != hoverX || y != hoverY) { hoverX = x; hoverY = y; RefreshVisuals(); }
        }
        else if (hoverX != -1) { hoverX = hoverY = -1; RefreshVisuals(); }
    }

    bool MouseCell(out int x, out int y)
    {
        x = y = -1;
        RaycastHit hit;
        if (!Physics.Raycast(boardCamera.ScreenPointToRay(Input.mousePosition), out hit, 100)) return false;
        var cell = hit.collider.GetComponent<OpenAW3DCell>();
        if (cell == null) return false;
        x = cell.x; y = cell.y;
        return true;
    }

    Tank UnitAt(int x, int y) { return tanks.Find(t => t.x == x && t.y == y); }
    bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }
    bool Passable(int x, int y) { return InBounds(x, y) && ground[x, y] != Ground.Water; }

    void BuildReachable(Tank tank)
    {
        reachable.Clear(); parents.Clear();
        var start = new Vector2Int(tank.x, tank.y);
        var distance = new Dictionary<Vector2Int, int> { [start] = 0 };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(start); reachable.Add(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            for (int i = 0; i < dx.Length; i++)
            {
                var n = new Vector2Int(p.x + dx[i], p.y + dy[i]);
                if (!Passable(n.x, n.y) || distance.ContainsKey(n) || UnitAt(n.x, n.y) != null) continue;
                if (dx[i] != 0 && dy[i] != 0 &&
                    !Passable(p.x + dx[i], p.y) && !Passable(p.x, p.y + dy[i])) continue;
                if (distance[p] + 1 > MoveRange) continue;
                if (Vector2Int.Distance(start, n) > MoveRange) continue;
                distance[n] = distance[p] + 1; parents[n] = p;
                reachable.Add(n); queue.Enqueue(n);
            }
        }
    }

    void HandleWorldClick(int x, int y)
    {
        if (movingAnimation) return;
        var clicked = UnitAt(x, y);
        if (command == CommandState.Aim)
        {
            if (clicked != null && clicked.owner != current && Adjacent(selected, clicked)) Attack(selected, clicked);
            return;
        }
        if (command == CommandState.Move && selected != null)
        {
            if (clicked == selected) { command = CommandState.Actions; RefreshVisuals(); return; }
            if (clicked != null && clicked.owner == current && !clicked.acted) { Select(clicked); return; }
            if (reachable.Contains(new Vector2Int(x, y)) && clicked == null)
            {
                var route = PathTo(new Vector2Int(x, y));
                originX = selected.x; originY = selected.y;
                selected.x = x; selected.y = y; moved = x != originX || y != originY;
                selected.visual.GetComponent<OpenAW3DCell>().x = x; selected.visual.GetComponent<OpenAW3DCell>().y = y;
                if (moved) StartCoroutine(Drive(selected, route));
                else { command = CommandState.Actions; RefreshVisuals(); }
                return;
            }
            return;
        }
        if (command == CommandState.Actions) return;
        if (clicked != null && clicked.owner == current && !clicked.acted) { Select(clicked); return; }
        var building = buildings[x, y];
        if (building != null && building.type == Structure.Base && building.owner == current && clicked == null)
        { command = CommandState.Buy; originX = x; originY = y; return; }
        selected = null; command = CommandState.Options; RefreshVisuals();
    }

    void Select(Tank unit)
    {
        selected = unit; moved = false; originX = unit.x; originY = unit.y;
        command = CommandState.Move; BuildReachable(unit); RefreshVisuals();
    }

    List<Vector2Int> PathTo(Vector2Int target)
    {
        var path = new List<Vector2Int> { target };
        while (parents.ContainsKey(path[path.Count - 1])) path.Add(parents[path[path.Count - 1]]);
        path.Reverse();
        return path;
    }

    IEnumerator Drive(Tank tank, List<Vector2Int> path)
    {
        movingAnimation = true;
        command = CommandState.None;
        routeLine.positionCount = 0;
        tank.visual.GetComponent<Collider>().enabled = false;
        for (int i = 1; i < path.Count; i++)
        {
            var target = new Vector3(path[i].x, .38f, path[i].y);
            while (Vector3.Distance(tank.visual.transform.position, target) > .015f)
            {
                tank.visual.transform.position = Vector3.MoveTowards(tank.visual.transform.position, target, 5f * Time.deltaTime);
                yield return null;
            }
            tank.visual.transform.position = target;
        }
        tank.visual.GetComponent<Collider>().enabled = true;
        movingAnimation = false;
        command = CommandState.Actions;
        RefreshVisuals();
    }

    void Cancel()
    {
        if (screen != ScreenState.Play) return;
        if (command == CommandState.Aim) { command = CommandState.Actions; return; }
        if (command == CommandState.Actions && moved && selected != null)
        {
            selected.x = originX; selected.y = originY;
            selected.visual.transform.position = new Vector3(originX, .38f, originY);
            selected.visual.GetComponent<OpenAW3DCell>().x = originX; selected.visual.GetComponent<OpenAW3DCell>().y = originY;
            moved = false; command = CommandState.Move; BuildReachable(selected); RefreshVisuals(); return;
        }
        selected = null; command = CommandState.None; RefreshVisuals();
    }

    bool Adjacent(Tank a, Tank b)
    { return a != null && b != null && Mathf.Abs(a.x - b.x) <= 1 && Mathf.Abs(a.y - b.y) <= 1 && (a.x != b.x || a.y != b.y); }
    bool CanFire()
    { return selected != null && tanks.Exists(t => t.owner != current && Adjacent(selected, t)); }

    void Attack(Tank attacker, Tank defender)
    {
        int damage = Mathf.Max(1, Mathf.CeilToInt(attacker.hp * .5f));
        defender.hp -= damage;
        if (defender.hp <= 0) DestroyTank(defender);
        else
        {
            attacker.hp -= Mathf.Max(1, Mathf.CeilToInt(defender.hp * .2f));
            if (attacker.hp <= 0) DestroyTank(attacker);
        }
        CheckWinner();
        FinishAction();
    }

    void DestroyTank(Tank tank)
    {
        var burst = Shape(PrimitiveType.Sphere, boardRoot, new Vector3(tank.x, .7f, tank.y), Vector3.one * .8f);
        burst.GetComponent<Renderer>().material.color = new Color(1, .55f, .1f);
        effects.Add(burst); Destroy(burst, .4f);
        tanks.Remove(tank); Destroy(tank.visual);
        if (selected == tank) selected = null;
    }

    void Capture()
    {
        var building = selected == null ? null : buildings[selected.x, selected.y];
        if (building == null || building.owner == current) return;
        building.capture -= selected.hp;
        if (building.capture <= 0) { building.owner = current; building.capture = 20; CheckWinner(); }
        FinishAction();
    }

    void FinishAction()
    {
        if (selected != null) selected.acted = true;
        selected = null; moved = false; command = CommandState.None; reachable.Clear(); RefreshVisuals();
    }

    void Buy()
    {
        if (funds[current] < TankPrice || UnitAt(originX, originY) != null) return;
        var baseBuilding = buildings[originX, originY];
        if (baseBuilding == null || baseBuilding.type != Structure.Base || baseBuilding.owner != current) return;
        funds[current] -= TankPrice;
        MakeTank(originX, originY, current, true);
        command = CommandState.None; RefreshVisuals();
    }

    void EndTurn()
    {
        foreach (var b in buildings)
        {
            if (b == null) continue;
            if (b.owner == current) funds[current] += 3000;
            if (UnitAt(b.x, b.y) == null || UnitAt(b.x, b.y).owner == b.owner)
                b.capture = Mathf.Min(20, b.capture + 2);
        }
        current = 1 - current;
        if (current == 0) day++;
        bannerUntil = Time.time + 1.6f;
        foreach (var t in tanks)
        {
            if (t.owner != current) continue;
            t.acted = false;
            var b = buildings[t.x, t.y];
            if (b != null && b.owner == current)
                t.hp = Mathf.Min(10, t.hp + (b.type == Structure.Base ? 2 : 1));
        }
        selected = null; moved = false; command = CommandState.None; reachable.Clear(); RefreshVisuals();
    }

    void CheckWinner()
    {
        for (int side = 0; side < 2; side++)
        {
            bool hasUnit = tanks.Exists(t => t.owner == side);
            bool hasBuilding = false;
            foreach (var b in buildings) if (b != null && b.owner == side) { hasBuilding = true; break; }
            if (!hasUnit && !hasBuilding) { winner = 1 - side; screen = ScreenState.Win; return; }
        }
    }

    Color SideColor(int side)
    { return side == 0 ? new Color(.87f, .23f, .2f) : side == 1 ? new Color(.2f, .49f, .93f) : new Color(.8f, .8f, .72f); }

    void RefreshVisuals()
    {
        if (routeLine != null)
        {
            if (command == CommandState.Move && selected != null && reachable.Contains(new Vector2Int(hoverX, hoverY)))
            {
                var path = PathTo(new Vector2Int(hoverX, hoverY));
                routeLine.positionCount = path.Count;
                for (int i = 0; i < path.Count; i++) routeLine.SetPosition(i, new Vector3(path[i].x, .41f, path[i].y));
            }
            else routeLine.positionCount = 0;
        }
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (tiles[x, y] == null) continue;
            Color color;
            switch (ground[x, y])
            {
                case Ground.Water: color = new Color(.15f, .41f, .69f); break;
                case Ground.Road: color = new Color(.58f, .53f, .42f); break;
                case Ground.Bridge: color = new Color(.7f, .55f, .3f); break;
                default: color = new Color(.38f, .62f, .34f); break;
            }
            if (command == CommandState.Move && reachable.Contains(new Vector2Int(x, y))) color = Color.Lerp(color, Color.yellow, .5f);
            if (x == hoverX && y == hoverY) color = Color.Lerp(color, Color.white, .35f);
            tiles[x, y].GetComponent<Renderer>().material.color = color;
            var b = buildings[x, y];
            if (b != null && b.visual != null)
            {
                var c = SideColor(b.owner);
                foreach (var r in b.visual.GetComponentsInChildren<Renderer>()) r.material.color = c;
                b.visual.transform.localScale = UnitAt(x, y) == null ? Vector3.one : Vector3.one * .75f;
            }
        }
        foreach (var t in tanks)
        {
            var c = SideColor(t.owner);
            if (t.acted) c *= .55f;
            foreach (var r in t.visual.GetComponentsInChildren<Renderer>()) r.material.color = c;
        }
    }

    void OnGUI()
    {
        if (heading == null) SetupStyles();
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 800f, Screen.height / 600f, 1));
        if (screen == ScreenState.Title)
        {
            GUI.Box(new Rect(155, 105, 490, 390), "");
            GUI.Label(new Rect(170, 140, 460, 90), "OPEN AW3D", heading);
            GUI.Label(new Rect(190, 225, 420, 48), "3D turn based tank tactics", body);
            if (GUI.Button(new Rect(260, 300, 280, 52), "Play (Hotseat Multiplayer)", button)) StartMatch();
            if (GUI.Button(new Rect(260, 370, 280, 48), "Quit", button)) Application.Quit();
        }
        else if (screen == ScreenState.Tutorial)
        {
            GUI.Box(new Rect(110, 80, 580, 440), "");
            GUI.Label(new Rect(130, 100, 540, 70), "HOW TO PLAY", heading);
            GUI.Label(new Rect(150, 175, 500, 250),
                "Red and Blue take turns. Move tanks, attack adjacent enemies and capture buildings.\n\n" +
                "Capture takes repeated orders: each turn reduces 20 capture points by your tank's health. " +
                "Your buildings earn 3000 each turn. Buy a tank at your base for 6000. " +
                "Friendly buildings heal parked tanks.\n\nLeft click to select / confirm. Right click to cancel. WASD to pan; wheel to zoom.", body);
            if (GUI.Button(new Rect(275, 445, 250, 48), "Day 1  •  Begin", button)) screen = ScreenState.Play;
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { screen = ScreenState.Play; Event.current.Use(); }
        }
        else if (screen == ScreenState.Win)
        {
            GUI.Box(new Rect(0, 0, 800, 600), "");
            GUI.Label(new Rect(110, 180, 580, 100), (winner == 0 ? "RED" : "BLUE") + " WINS!", heading);
            if (GUI.Button(new Rect(285, 320, 230, 55), "Exit Map", button)) ExitMap();
        }
        else DrawGameGUI();
        GUI.matrix = oldMatrix;
    }

    void ExitMap()
    {
        StopAllCoroutines();
        movingAnimation = false;
        if (boardRoot != null) Destroy(boardRoot.gameObject);
        boardRoot = null; screen = ScreenState.Title; command = CommandState.None; selected = null;
    }

    void DrawGameGUI()
    {
        GUI.Box(new Rect(0, 0, 800, 55), "");
        GUI.color = SideColor(current);
        GUI.Label(new Rect(12, 7, 255, 40), (current == 0 ? "RED" : "BLUE") + "  •  Day " + day, body);
        GUI.color = Color.white;
        GUI.Label(new Rect(270, 7, 175, 40), "$ " + funds[current], body);
        GUI.Label(new Rect(455, 7, 335, 40), "Left: select / confirm    Right: cancel", small);
        if (Time.time < bannerUntil)
            GUI.Label(new Rect(250, 90, 300, 75), "Day " + day, heading);
        foreach (var tank in tanks)
        {
            if (tank.hp >= 10) continue;
            var point = boardCamera.WorldToScreenPoint(new Vector3(tank.x, .95f, tank.y));
            if (point.z <= 0) continue;
            GUI.color = SideColor(tank.owner);
            GUI.Label(new Rect(point.x * 800f / Screen.width - 12,
                (Screen.height - point.y) * 600f / Screen.height - 20, 38, 28), tank.hp.ToString(), small);
            GUI.color = Color.white;
        }

        GUI.Box(new Rect(574, 468, 220, 126), "");
        string info = hoverX < 0 ? "Hover over a tile" : TileInfo(hoverX, hoverY);
        GUI.Label(new Rect(588, 478, 195, 110), info, small);

        if (command == CommandState.Options)
        {
            GUI.Box(new Rect(305, 225, 190, 145), "Options");
            if (GUI.Button(new Rect(325, 260, 150, 38), "End Turn", button)) EndTurn();
            if (GUI.Button(new Rect(325, 307, 150, 38), "Exit Map", button)) ExitMap();
        }
        else if (command == CommandState.Buy)
        {
            GUI.Box(new Rect(286, 225, 228, 150), "Base");
            GUI.enabled = funds[current] >= TankPrice && UnitAt(originX, originY) == null;
            if (GUI.Button(new Rect(308, 266, 184, 42), "Tank  $6000", button)) Buy();
            GUI.enabled = true;
            if (GUI.Button(new Rect(308, 320, 184, 32), "Cancel", button)) Cancel();
        }
        else if (command == CommandState.Actions && selected != null)
        {
            GUI.Box(new Rect(270, 415, 260, 175), "Tank actions");
            GUI.enabled = CanFire();
            if (GUI.Button(new Rect(287, 444, 226, 34), "Fire", button)) command = CommandState.Aim;
            GUI.enabled = buildings[selected.x, selected.y] != null && buildings[selected.x, selected.y].owner != current;
            if (GUI.Button(new Rect(287, 481, 226, 34), "Capture", button)) Capture();
            GUI.enabled = true;
            if (GUI.Button(new Rect(287, 520, 226, 34), "Wait", button)) FinishAction();
        }
        else if (command == CommandState.Aim)
            GUI.Label(new Rect(248, 64, 300, 42), "Select an adjacent enemy", body);
        else if (command == CommandState.Move)
            GUI.Label(new Rect(225, 64, 350, 42), "Choose a highlighted tile", body);

        // Buttons above consume their mouse event, so only unused left clicks reach the board.
        var e = Event.current;
        if (!movingAnimation && e.type == EventType.MouseDown && e.button == 0)
        {
            var p = new Vector2(Input.mousePosition.x * 800f / Screen.width,
                (Screen.height - Input.mousePosition.y) * 600f / Screen.height);
            bool blocked = p.y < 58 || (p.x > 570 && p.y > 465) ||
                ((command == CommandState.Options || command == CommandState.Buy) && p.x > 265 && p.x < 535 && p.y > 215 && p.y < 380) ||
                (command == CommandState.Actions && p.x > 260 && p.x < 540 && p.y > 405);
            if (!blocked)
            {
                int x, y;
                if (MouseCell(out x, out y)) HandleWorldClick(x, y);
                e.Use();
            }
        }
    }

    string TileInfo(int x, int y)
    {
        string terrain = ground[x, y].ToString();
        var b = buildings[x, y];
        var t = UnitAt(x, y);
        if (b != null) terrain += "\n" + b.type + " • " + (b.owner < 0 ? "Neutral" : b.owner == 0 ? "Red" : "Blue") + " • Capture " + b.capture + "/20";
        if (t != null) terrain += "\n" + (t.owner == 0 ? "Red" : "Blue") + " tank • HP " + t.hp + "/10" + (t.acted ? " • acted" : "");
        return "(" + x + ", " + y + ")  " + terrain;
    }
}
