using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Behavior Parameters:
//   Behavior Name: ShooterTask
//   Vector Observation Space Size: 14
//   Continuous Actions: 2   (0 = előre/hátra, 1 = forgás)
//   Discrete Branches: 1, Branch 0 Size: 2   (0 = nem lő, 1 = lő)
public class ShooterAgent : Agent
{
    const int MaxZones = 4;

    [Header("Hivatkozások")]
    public GameManager gameManager;
    public Transform firePoint;

    [Header("Mozgás")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 180f;

    [Header("Lövés (azonnali sugár)")]
    public float shootRange = 30f;
    [Tooltip("Hány fizikai lépésenként lőhet (50 lépés = 1 mp)")]
    public int shootCooldownSteps = 10;
    public bool drawShots = true;

    [Header("Tanítás")]
    [Tooltip("Ha nem a curriculum dönt (pl. Heuristic teszt), legyenek-e ellenségek")]
    public bool spawnEnemiesDefault = false;

    [Header("Jutalmak")]
    public float zoneEnterReward = 1.0f;     // aktív zónába belépés
    public float hitReward = 1.0f;           // találat az aktív zónából
    public float wrongZoneHitPenalty = -0.2f;// találat a zónán kívülről
    public float clearBonus = 1.0f;          // minden ellenség lelőve
    public float shotPenalty = -0.01f;       // minden lövés ára
    public float shapingScale = 1.0f;        // közeledés az aktív zónához

    private Rigidbody rb;
    private float startY;
    private int lastShotStep;
    private bool enteredActiveZone;
    private float prevDistance;

    public override void Initialize()
    {
        // Ha az Inspectorban nincs bekötve: először a saját pályán (szülő objektumban) keressük,
        // így több lemásolt pálya esetén sem keveredik össze
        if (gameManager == null && transform.parent != null)
            gameManager = transform.parent.GetComponentInChildren<GameManager>();
        if (gameManager == null) gameManager = FindObjectOfType<GameManager>();
        if (gameManager == null)
            Debug.LogError("ShooterAgent: nem találok GameManager-t! Húzd be a 'Game Manager' objektumot az ágens Game Manager mezőjébe.");

        rb = GetComponent<Rigidbody>();
        if (rb == null)
            Debug.LogError("ShooterAgent: nincs Rigidbody az ágensen! Adj hozzá egyet.");
        rb.freezeRotation = true;
        startY = transform.position.y;
        if (MaxStep == 0) MaxStep = 3000; // epizód mindig érjen véget
    }

    public override void OnEpisodeBegin()
    {
        float enemiesParam = Academy.Instance.EnvironmentParameters
            .GetWithDefault("spawn_enemies", spawnEnemiesDefault ? 1f : 0f);
        gameManager.ResetArea(enemiesParam > 0.5f);

        // Véletlen kezdőhely, mindig az aktív zónán KÍVÜL
        Vector3 p;
        int tries = 0;
        do
        {
            p = gameManager.RandomPointInArena(1.5f);
            tries++;
        } while (gameManager.IsInActiveZone(p) && tries < 30);
        p.y = startY;

        transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        rb.position = p;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        lastShotStep = -shootCooldownSteps;
        ResetZoneTracking();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1) Melyik zóna aktív (one-hot) - 4
        int z = gameManager.ActiveZoneIndex;
        for (int i = 0; i < MaxZones; i++) sensor.AddObservation(i == z ? 1f : 0f);

        Bounds a = gameManager.ArenaBounds;
        float arenaDiag = new Vector2(a.size.x, a.size.z).magnitude;

        // 2) Saját pozíció normalizálva (-1..1) - 2
        Vector3 rel = transform.position - a.center;
        sensor.AddObservation(rel.x / a.extents.x);
        sensor.AddObservation(rel.z / a.extents.z);

        // 3) Merre néz - 2
        sensor.AddObservation(transform.forward.x);
        sensor.AddObservation(transform.forward.z);

        // 4) Az aktív zóna iránya SAJÁT nézőpontból + távolsága - 3
        Vector3 toZone = gameManager.ActiveZoneCenter - transform.position;
        toZone.y = 0f;
        Vector3 localDir = transform.InverseTransformDirection(toZone.normalized);
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);
        sensor.AddObservation(Mathf.Clamp01(toZone.magnitude / arenaDiag));

        // 5) Bent van-e, tud-e lőni, hány ellenség maradt - 3
        sensor.AddObservation(gameManager.IsInActiveZone(transform.position) ? 1f : 0f);
        sensor.AddObservation(CanShoot() ? 1f : 0f);
        sensor.AddObservation(gameManager.enemiesPerZone > 0
            ? (float)gameManager.EnemiesAlive / gameManager.enemiesPerZone : 0f);
        // Összesen: 14
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float move = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        int shoot = actions.DiscreteActions[0];

        // Mozgás + forgás
        Vector3 v = transform.forward * move * moveSpeed;
        v.y = rb.linearVelocity.y;
        rb.linearVelocity = v;
        transform.Rotate(Vector3.up, turn * rotationSpeed * Time.fixedDeltaTime);
        KeepInsideArena();

        // Idő múlása kicsit büntet -> siessen
        AddReward(-1f / MaxStep);

        if (shoot == 1 && CanShoot())
        {
            lastShotStep = StepCount;
            Shoot();
        }

        UpdateZoneProgress();
    }

    private bool CanShoot() => StepCount - lastShotStep >= shootCooldownSteps;

    private void Shoot()
    {
        AddReward(shotPenalty);

        Vector3 origin = firePoint != null ? firePoint.position : transform.position + Vector3.up * 0.5f;
        Vector3 dir = transform.forward;
        dir.y = 0f;
        dir.Normalize();

        RaycastHit[] hits = Physics.RaycastAll(origin, dir, shootRange, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (h1, h2) => h1.distance.CompareTo(h2.distance));

        GameObject hitEnemy = null;
        float hitDist = shootRange;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue; // saját magunk

            Enemy enemyComp = h.collider.GetComponentInParent<Enemy>();
            if (enemyComp != null || h.collider.CompareTag("Enemy"))
            {
                GameObject candidate = enemyComp != null ? enemyComp.gameObject : h.collider.gameObject;
                if (!gameManager.OwnsEnemy(candidate)) { hitDist = h.distance; break; } // másik pálya ellensége
                hitEnemy = candidate;
                hitDist = h.distance;
                break;
            }
            if (h.collider.isTrigger) continue; // zónák és más triggerek "átlátszók"

            hitDist = h.distance; // fal / akadály megállítja
            break;
        }

        if (drawShots)
            Debug.DrawRay(origin, dir * hitDist, hitEnemy != null ? Color.green : Color.red, 0.2f);

        if (hitEnemy == null) return;

        bool inZone = gameManager.IsInActiveZone(transform.position);
        bool cleared = gameManager.KillEnemy(hitEnemy);
        AddReward(inZone ? hitReward : wrongZoneHitPenalty);

        if (cleared)
        {
            if (inZone) AddReward(clearBonus);
            gameManager.ActivateNextZone();
            ResetZoneTracking();
        }
    }

    // Jutalom a zóna felé haladásért + a belépésért
    private void UpdateZoneProgress()
    {
        if (enteredActiveZone) return;

        Bounds a = gameManager.ArenaBounds;
        float arenaDiag = new Vector2(a.size.x, a.size.z).magnitude;
        float d = DistanceToActiveZone();
        AddReward(shapingScale * (prevDistance - d) / arenaDiag);
        prevDistance = d;

        if (gameManager.IsInActiveZone(transform.position))
        {
            enteredActiveZone = true;
            AddReward(zoneEnterReward);

            // 1. fázis (nincs ellenség): azonnal jön a következő zóna
            if (!gameManager.EnemiesEnabled)
            {
                gameManager.ActivateNextZone();
                ResetZoneTracking();
            }
        }
    }

    private void ResetZoneTracking()
    {
        enteredActiveZone = false;
        prevDistance = DistanceToActiveZone();
    }

    private float DistanceToActiveZone()
    {
        Vector3 d = gameManager.ActiveZoneCenter - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // Biztonsági háló, hogy ne essen le a pályáról (a falak a fő határ)
    private void KeepInsideArena()
    {
        Bounds a = gameManager.ArenaBounds;
        const float m = 0.5f;
        Vector3 p = rb.position;
        float cx = Mathf.Clamp(p.x, a.min.x + m, a.max.x - m);
        float cz = Mathf.Clamp(p.z, a.min.z + m, a.max.z - m);
        if (!Mathf.Approximately(cx, p.x) || !Mathf.Approximately(cz, p.z))
        {
            rb.position = new Vector3(cx, p.y, cz);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var c = actionsOut.ContinuousActions;
        c[0] = 0f; // előre/hátra
        c[1] = 0f; // forgás
        if (Input.GetKey(KeyCode.W)) c[0] = 1f;
        if (Input.GetKey(KeyCode.S)) c[0] = -1f;
        if (Input.GetKey(KeyCode.A)) c[1] = -1f;
        if (Input.GetKey(KeyCode.D)) c[1] = 1f;

        var d = actionsOut.DiscreteActions;
        d[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }
}