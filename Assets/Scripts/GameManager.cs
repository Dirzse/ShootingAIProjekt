using System.Collections.Generic;
using UnityEngine;

// Egyetlen "igazságforrás" a zónákhoz és az ellenségekhez.
// (A régi ZoneManager.cs-t törölni kell, ez váltja ki.)
public class GameManager : MonoBehaviour
{
    [Header("Zónák - a lista sorrendje = zóna index (0..3)")]
    public List<BoxCollider> allZones;

    [Header("Ellenségek")]
    public GameObject enemyPrefab;
    public int enemiesPerZone = 3;
    [Tooltip("Milyen magasan legyen az ellenség a zóna aljához képest")]
    public float enemySpawnHeight = 0.5f;
    [Tooltip("Ennyivel a zóna szélétől beljebb spawnolnak")]
    public float spawnEdgeMargin = 1.0f;

    [Header("Megjelenítés (csak nézéshez, a tanítást nem befolyásolja)")]
    [Tooltip("Színes lap a zónák alján: az aktív zóna világít")]
    public bool showZoneMarkers = true;
    public Color activeZoneColor = new Color(0.2f, 1f, 0.2f);
    public Color inactiveZoneColor = new Color(0.35f, 0.35f, 0.35f);
    [Tooltip("Konzolba írja a zónaváltásokat (tanításnál kapcsold ki, mert sok)")]
    public bool logZoneChanges = false;
    [Tooltip("Bal felső sarokban kiírja az aktív zónát")]
    public bool showOnScreenInfo = true;

    public int ActiveZoneIndex { get; private set; } = -1;
    public bool EnemiesEnabled { get; private set; }
    public int EnemiesAlive => activeEnemies.Count;

    private readonly List<GameObject> activeEnemies = new List<GameObject>();
    private readonly List<Renderer> zoneMarkers = new List<Renderer>();
    private Bounds arenaBounds;
    private bool initialized;

    // A teljes pálya = az összes zóna együtt
    public Bounds ArenaBounds
    {
        get { EnsureInit(); return arenaBounds; }
    }

    private void EnsureInit()
    {
        if (initialized) return;
        if (allZones == null || allZones.Count == 0)
        {
            Debug.LogError("GameManager: az allZones lista üres! Húzd be a 4 zónát.");
            return;
        }
        arenaBounds = allZones[0].bounds;
        for (int i = 1; i < allZones.Count; i++) arenaBounds.Encapsulate(allZones[i].bounds);
        if (showZoneMarkers) CreateZoneMarkers();
        initialized = true;
    }

    // Minden zóna aljára egy vékony színes lap (collider NÉLKÜL, hogy ne zavarja a sugarakat)
    private void CreateZoneMarkers()
    {
        foreach (var zone in allZones)
        {
            Bounds b = zone.bounds;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = zone.name + "_Marker";
            Destroy(marker.GetComponent<Collider>());
            // A padló magasságát lefelé lőtt sugárral keressük meg (a zóna doboza lelóghat a padló alá)
            float floorY = arenaBounds.min.y;
            Vector3 from = new Vector3(b.center.x, b.max.y + 5f, b.center.z);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, b.size.y + 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Enemy>() != null) continue;
                if (h.collider.GetComponentInParent<ShooterAgent>() != null) continue;
                if (h.distance < best) { best = h.distance; floorY = h.point.y; }
            }
            marker.transform.position = new Vector3(b.center.x, floorY + 0.03f, b.center.z);
            marker.transform.localScale = new Vector3(b.size.x * 0.96f, 0.02f, b.size.z * 0.96f);
            Renderer r = marker.GetComponent<Renderer>();
            r.material.color = inactiveZoneColor;
            zoneMarkers.Add(r);
        }
    }

    private void UpdateZoneMarkers()
    {
        for (int i = 0; i < zoneMarkers.Count; i++)
        {
            if (zoneMarkers[i] != null)
                zoneMarkers[i].material.color = (i == ActiveZoneIndex) ? activeZoneColor : inactiveZoneColor;
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenInfo || ActiveZoneIndex < 0) return;
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        string text = "Aktív zóna: " + allZones[ActiveZoneIndex].name;
        if (EnemiesEnabled) text += "   |   Ellenség: " + EnemiesAlive;
        GUI.Label(new Rect(15, 10, 600, 35), text, style);
    }

    // Epizód elején hívja az ágens
    public void ResetArea(bool spawnEnemies)
    {
        EnsureInit();
        EnemiesEnabled = spawnEnemies;
        ClearEnemies();
        ActiveZoneIndex = -1;
        ActivateNextZone();
    }

    // Új aktív zóna (mindig más, mint az előző) + ellenségek, ha be vannak kapcsolva
    public void ActivateNextZone()
    {
        EnsureInit();
        int next = Random.Range(0, allZones.Count);
        if (allZones.Count > 1)
        {
            while (next == ActiveZoneIndex) next = Random.Range(0, allZones.Count);
        }
        ActiveZoneIndex = next;
        UpdateZoneMarkers();
        if (logZoneChanges) Debug.Log("Aktív zóna: " + allZones[next].name);

        ClearEnemies();
        if (EnemiesEnabled) SpawnEnemies(allZones[next], enemiesPerZone);
    }

    public Vector3 ActiveZoneCenter => allZones[ActiveZoneIndex].bounds.center;

    // Csak X/Z alapján nézzük, a magasság nem számít
    public bool IsInActiveZone(Vector3 worldPos)
    {
        if (ActiveZoneIndex < 0) return false;
        Bounds b = allZones[ActiveZoneIndex].bounds;
        return worldPos.x >= b.min.x && worldPos.x <= b.max.x &&
               worldPos.z >= b.min.z && worldPos.z <= b.max.z;
    }

    public Vector3 RandomPointInArena(float margin)
    {
        Bounds a = ArenaBounds;
        return new Vector3(
            Random.Range(a.min.x + margin, a.max.x - margin),
            a.center.y,
            Random.Range(a.min.z + margin, a.max.z - margin));
    }

    // Ehhez a pályához tartozik-e az ellenség (több párhuzamos pályánál fontos)
    public bool OwnsEnemy(GameObject enemy) => activeEnemies.Contains(enemy);

    // Igaz, ha ez volt az utolsó élő ellenség
    public bool KillEnemy(GameObject enemy)
    {
        bool wasTracked = activeEnemies.Remove(enemy);
        enemy.SetActive(false); // azonnal tűnjön el (a Destroy csak a frame végén hat)
        Destroy(enemy);
        return wasTracked && activeEnemies.Count == 0;
    }

    private void SpawnEnemies(BoxCollider zone, int count)
    {
        if (enemyPrefab == null) return;
        Bounds b = zone.bounds;
        float mx = Mathf.Min(spawnEdgeMargin, b.extents.x * 0.9f);
        float mz = Mathf.Min(spawnEdgeMargin, b.extents.z * 0.9f);

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(b.min.x + mx, b.max.x - mx),
                b.min.y + enemySpawnHeight,
                Random.Range(b.min.z + mz, b.max.z - mz));
            GameObject enemy = Instantiate(enemyPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            activeEnemies.Add(enemy);
        }
    }

    private void ClearEnemies()
    {
        foreach (var e in activeEnemies)
        {
            if (e != null) Destroy(e);
        }
        activeEnemies.Clear();
    }
}