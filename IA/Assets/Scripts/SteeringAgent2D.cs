using UnityEngine;
using UnityEngine.InputSystem;

public class SteeringAgent2D : MonoBehaviour
{
    public enum BehaviorType { SeekArrival, Flee, Wander, Pursuit, Evade }

    [Header("Configuración del Agente")]
    public BehaviorType currentBehavior;
    public Key activationKey; // Tecla del nuevo Input System (Ej: Digit1, Digit2, etc.)

    [Header("Referencias de Objetivos")]
    public Transform targetTransform; // Para Seek, Flee
    public SteeringAgent2D targetAgent;  // Referencia al otro agente (Pursuit / Evade)

    [Header("Parámetros de Movimiento")]
    public float maxSpeed = 5.0f;
    public float maxForce = 15.0f;
    public float mass = 1.0f;
    public float arrivalRadius = 2.0f;

    [Header("Obstacle Avoidance (2D)")]
    public LayerMask obstacleLayer;
    public float sensorLength = 2.5f;
    public float avoidWeight = 30.0f;

    [Header("Parámetros de Wander")]
    public float wanderRadius = 2.0f;
    public float wanderDistance = 3.0f;
    public float wanderJitter = 1.0f;
    private Vector2 wanderTarget;

    // Estado interno
    private bool isActive = false;
    [HideInInspector] public Vector2 velocity = Vector2.zero;
    private SpriteRenderer agentRenderer;

    void Start()
    {
        agentRenderer = GetComponent<SpriteRenderer>();
        SetAgentActive(false); // Inician desactivados
        wanderTarget = Random.insideUnitCircle * wanderRadius;
    }

    void Update()
    {
        // Sistema de activación independiente con el nuevo Input System
        //if (Keyboard.current != null && Keyboard.current[activationKey].wasPressedThisFrame)
        //{
        //    isActive = !isActive;
        //    SetAgentActive(isActive);
        //}

        if (!isActive) return;

        Vector2 steering = Vector2.zero;

        switch (currentBehavior)
        {
            case BehaviorType.SeekArrival:
                if (targetTransform != null)
                    steering = SeekWithArrival(targetTransform.position);
                break;

            case BehaviorType.Flee:
                Vector2 mouseWorldPos = GetMouseWorldPosition();
                steering = Flee(mouseWorldPos);
                break;

            case BehaviorType.Wander:
                steering = Wander();
                break;

            case BehaviorType.Pursuit:
                if (targetAgent != null)
                    steering = Pursuit(targetAgent);
                break;

            case BehaviorType.Evade:
                if (targetAgent != null)
                    steering = Evade(targetAgent);
                break;
        }

        // Agregar evasión de obstáculos 2D
        Vector2 avoidance = ObstacleAvoidance();
        steering += avoidance * avoidWeight;

        // --- CORRECCIÓN DE ESTABILIDAD ---
        // Limitar la fuerza de dirección para evitar picos de aceleración ("salir volando")
        steering = Vector2.ClampMagnitude(steering, maxForce);
        Vector2 acceleration = steering / mass;

        velocity += acceleration * Time.deltaTime;
        velocity = Vector2.ClampMagnitude(velocity, maxSpeed);

        if (velocity.sqrMagnitude > 0.001f)
        {
            transform.position += (Vector3)(velocity * Time.deltaTime);

            // Rotación suave del sprite (Ajusta el -90f si tu sprite apunta hacia la derecha original)
            float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    void SetAgentActive(bool active)
    {
        isActive = active;
        if (agentRenderer != null)
        {
            agentRenderer.color = active ? Color.green : Color.gray;
        }
        if (!active)
        {
            velocity = Vector2.zero; // Limpiar velocidad al apagarse
        }
    }

    Vector2 SeekWithArrival(Vector2 targetPos)
    {
        Vector2 currentPos = transform.position;
        Vector2 toTarget = targetPos - currentPos;
        float distance = toTarget.magnitude;

        if (distance < 0.1f) return -velocity; // Evita vibración si ya llegó

        Vector2 desiredVelocity = toTarget.normalized * maxSpeed;

        if (distance < arrivalRadius)
        {
            desiredVelocity *= (distance / arrivalRadius); // Desaceleración suave
        }

        return desiredVelocity - velocity;
    }

    Vector2 Flee(Vector2 targetPos)
    {
        Vector2 currentPos = transform.position;
        Vector2 toTarget = currentPos - targetPos;
        float distance = toTarget.magnitude;

        if (distance > 15f) return Vector2.zero; // Si el mouse está muy lejos, no gasta fuerza huyendo

        Vector2 desiredVelocity = toTarget.normalized * maxSpeed;
        return desiredVelocity - velocity;
    }

    Vector2 Wander()
    {
        wanderTarget += new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * wanderJitter;
        wanderTarget.Normalize();
        wanderTarget *= wanderRadius;

        Vector2 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : (Vector2)transform.up;
        Vector2 targetWorld = (Vector2)transform.position + forward * wanderDistance + wanderTarget;

        Vector2 desiredVelocity = (targetWorld - (Vector2)transform.position).normalized * maxSpeed;
        return desiredVelocity - velocity;
    }

    Vector2 Pursuit(SteeringAgent2D evader)
    {
        if (evader == null) return Vector2.zero;

        Vector2 toEvader = (Vector2)evader.transform.position - (Vector2)transform.position;
        float lookAheadDst = toEvader.magnitude / (maxSpeed + evader.velocity.magnitude + 0.001f);
        Vector2 futurePosition = (Vector2)evader.transform.position + evader.velocity * lookAheadDst;

        return SeekWithArrival(futurePosition);
    }

    Vector2 Evade(SteeringAgent2D pursuer)
    {
        if (pursuer == null) return Vector2.zero;

        Vector2 toPursuer = (Vector2)pursuer.transform.position - (Vector2)transform.position;
        float lookAheadDst = toPursuer.magnitude / (maxSpeed + pursuer.velocity.magnitude + 0.001f);
        Vector2 futurePosition = (Vector2)pursuer.transform.position + pursuer.velocity * lookAheadDst;

        return Flee(futurePosition);
    }

    Vector2 ObstacleAvoidance()
    {
        Vector2 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : (Vector2)transform.up;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, forward, sensorLength, obstacleLayer);

        if (hit.collider != null)
        {
            // Fuerza lateral de escape perpendicular al obstáculo
            Vector2 avoidanceForce = Vector2.Perpendicular(forward) * (hit.distance < 1f ? 2f : 1f);
            return avoidanceForce.normalized * maxSpeed;
        }
        return Vector2.zero;
    }

    Vector2 GetMouseWorldPosition()
    {
        if (Mouse.current == null) return transform.position;
        Vector3 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);
        return new Vector2(mouseWorld.x, mouseWorld.y);
    }
}