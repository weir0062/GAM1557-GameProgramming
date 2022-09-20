using UnityEngine;


public class Player : MonoBehaviour
{
    public float OnGroundMoveAccel = 10.0f;
    public float OnGroundMaxSpeed = 10.0f;
    public float OnGroundStopEaseSpeed = 10.0f;

    public bool InstantStepUp = false;
    public float StepUpEaseSpeed = 10.0f;
    public float MinAllowedSurfaceAngle = 15.0f;

    public float JumpSpeed = 10.0f;
    public float PlayerRotationSpeed = 25.0f;

    public float GroundCheckStartOffsetY = 0.5f;
    public float CheckForGroundRadius = 0.5f;
    public float GroundResolutionOverlap = 0.05f;

    public float JumpPushOutOfGroundAmount = 0.5f;

    public GameObject FootLocationObj;

    public float VectorVisualizeScale = 2.0f;

    //Properties  very similar to getters and setters in C++ lingvo
    //Allow us to obtain acces to private variable
    public PlayerController Controller { get; set; }
    public Vector3 GroundVelocity { get; private set; }
    public Vector3 GroundAngularVelocity { get; private set; }
    public Vector3 GroundNormal { get; private set; }

    void Start()
    {
        //Set up the player for human control.

        if (!SetupHumanPlayer())
        {
            return;
        }

        //Initialing miscellaneous values
        m_GroundCheckMask = ~LayerMask.GetMask("Player", "Ignore Raycast");// This line will prevent hitting player object during our raycast session

        m_RigidBody = GetComponent<Rigidbody>();

        m_Velocity = Vector3.zero;

        m_AllowJump = true;

    }

    void FixedUpdate()
    {
        //Update velocity from physics system
        m_Velocity = m_RigidBody.velocity;

        //Update ground info
        UpdateGroundInfo();

        //Get input
        Controller.UpdateControls();

        Vector3 localMoveDir = Controller.GetMoveInput();
        //Get direction from controller
        localMoveDir.Normalize();

        bool isJumping = Controller.IsJumping();

        //Get rotation from controller
        Vector3 r = Controller.GetLookInput();  //X is equal to Y because we rotate around x axis with vertical mouse movement
        r.x = 0;// We want to lock rotation around y x axis   ---------->

        transform.Rotate(r * Time.deltaTime * PlayerRotationSpeed); //rotate object based on current mouse rotation

        //Update movement
        switch (m_MovementState)
        {
            case MovementState.OnGround:
                UpdateOnGround(localMoveDir, isJumping);
                break;

            case MovementState.InAir:
                break;

            case MovementState.Disable:
                break;

            default:
                DebugUtils.LogError("Invalid movement state: {0}", m_MovementState);
                break;
        }
    }

    public void UpdateStopping(float stopEaseSpeed)
    {
        //Ease down to the ground velocity to stop relative to the ground
        m_Velocity = MathUtils.LerpTo(stopEaseSpeed, m_Velocity, GroundVelocity, Time.fixedDeltaTime);
    }

    void UpdateGroundInfo()
    {
        //Clear ground info.  Doing this here can simplify the code a bit since we deal with cases where the
        //ground isn't found more easily
        GroundAngularVelocity = Vector3.zero;
        GroundVelocity = Vector3.zero;
        GroundNormal.Set(0.0f, 0.0f, 1.0f);

        //Check for the ground below the player
        m_CenterHeight = transform.position.y;

        float footHeight = FootLocationObj.transform.position.y;

        float halfCapsuleHeight = m_CenterHeight - footHeight;

        Vector3 rayStart = transform.position;
        rayStart.y += GroundCheckStartOffsetY;

        Vector3 rayDir = -Vector3.up;

        float rayDist = halfCapsuleHeight + GroundCheckStartOffsetY - CheckForGroundRadius;

        //Find all of the surfaces overlapping the sphere cast
        RaycastHit[] hitInfos = Physics.SphereCastAll(rayStart, CheckForGroundRadius, rayDir, rayDist, m_GroundCheckMask);

        //Get the closest surface that is acceptable to walk on.  The order of the 
        RaycastHit groundHitInfo = new RaycastHit();
        bool validGroundFound = false;
        float minGroundDist = float.MaxValue;


        // After we get all hit infos we loop through all of them
        foreach (RaycastHit hitInfo in hitInfos)
        {
            //Check the surface angle to see if it's acceptable to walk on.  
            //Also checking if the distance is zero I ran into a case where the sphere cast was hitting a wall and
            //returning weird resuls in the hit info.  Checking if the distance is greater than 0 eliminates this 
            //case. 
            float surfaceAngle = MathUtils.CalcVerticalAngle(hitInfo.normal);
            if (surfaceAngle < MinAllowedSurfaceAngle || hitInfo.distance <= 0.0f)
            {
                continue;
            }

            if (hitInfo.distance < minGroundDist)
            {
                minGroundDist = hitInfo.distance;

                groundHitInfo = hitInfo;

                validGroundFound = true;
            }
        }

        if (!validGroundFound)
        {
            if (m_MovementState != MovementState.Disable)
            {
                SetMovementState(MovementState.InAir);
            }
            return;
        }

        //Step up
        Vector3 bottomAtHitPoint = MathUtils.ProjectToBottomOfCapsule(
                groundHitInfo.point,
                transform.position,
                halfCapsuleHeight * 2.0f,
                CheckForGroundRadius
                );

        float stepUpAmount = groundHitInfo.point.y - bottomAtHitPoint.y;

        m_CenterHeight += stepUpAmount - GroundResolutionOverlap;

        //Setting Ground Normal based on object normal we cought trough rayCast.
        GroundNormal = groundHitInfo.normal;
        // Debug.Log("Ground normal is " + GroundNormal.ToString());
        //Set the movement state to be on ground
        if (m_MovementState != MovementState.Disable)
        {
            SetMovementState(MovementState.OnGround);
        }
    }

    void UpdateOnGround(Vector3 localMoveDir, bool isJumping)
    {
        
//        transform.position += localMoveDir * OnGroundMoveAccel * Time.fixedDeltaTime;

        if(localMoveDir.sqrMagnitude > MathUtils.CompareEpsilon)
        {
            Vector3 localVelocity = m_Velocity - GroundVelocity;

            Vector3 moveAccel = CalcMoveAccel(localMoveDir);
            moveAccel *= OnGroundMoveAccel;
            localVelocity += moveAccel * Time.deltaTime;
            localVelocity = Vector3.ClampMagnitude(localVelocity, OnGroundMaxSpeed);
            m_Velocity = localVelocity + GroundVelocity;


            Vector3 GroundTangent = moveAccel - Vector3.Project(moveAccel, GroundNormal);
            GroundTangent.Normalize();

            moveAccel = GroundTangent;

            Debug.DrawLine(transform.position, transform.position + moveAccel * VectorVisualizeScale, Color.magenta);

            Vector3 VelocityAlongMoveDir = Vector3.Project(localVelocity, moveAccel);

            if (Vector3.Dot(VelocityAlongMoveDir, moveAccel) > 0.0f)
            {
                localVelocity = MathUtils.LerpTo(OnGroundStopEaseSpeed, localVelocity, VelocityAlongMoveDir, Time.deltaTime);
            }
            else
            {
                localVelocity = MathUtils.LerpTo(OnGroundStopEaseSpeed, localVelocity, Vector3.zero, Time.deltaTime);

            }

            localVelocity *= OnGroundMoveAccel;
            localVelocity += moveAccel * Time.deltaTime;
            localVelocity = Vector3.ClampMagnitude(localVelocity, OnGroundMaxSpeed);
            m_Velocity = localVelocity + GroundVelocity;

        }
        else
        {
            UpdateStopping(OnGroundStopEaseSpeed);
        }
        if(isJumping)
        {
            ActivateJump();
        }
        else
        {
            m_AllowJump = true;
        }
        ApplyVelocity(m_Velocity);
    }


    void OnTriggerEnter(Collider other)
    {
        if(other.tag == "Bush")
        {
            GetComponent<Renderer>().material.color = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);
        }
    }

    void ApplyVelocity(Vector3 velocity)
    {
        Vector3 velocityDiff = velocity - m_RigidBody.velocity;

        m_RigidBody.AddForce(velocityDiff, ForceMode.VelocityChange);
    }

    void ActivateJump()
    {
        //The allowJump bool is to prevent the player from holding down the jump button to bounce up and down
        //Instead they will have to release the button first.
        if (m_AllowJump)
        {
            //Set the vertical speed to be the jump speed + the ground velocity
            m_Velocity.y = JumpSpeed + GroundVelocity.y;

            //This is to ensure that the player wont still be touching the ground after the jump
            transform.position += new Vector3(0.0f, JumpPushOutOfGroundAmount, 0.0f);

            m_AllowJump = false;

        }
    }

    Vector3 CalcMoveAccel(Vector3 localMoveDir)
    {
        Vector3 moveAccel = localMoveDir;

        //Transforms direction x, y, z from local space to world space.
        moveAccel = transform.TransformDirection(moveAccel);

        return moveAccel;
    }

    void SetMovementState(MovementState movementState)
    {
        switch (movementState)
        {
            case MovementState.OnGround:
                break;

            case MovementState.InAir:
                break;

            case MovementState.Disable:
                m_Velocity = Vector3.zero;
                ApplyVelocity(m_Velocity);
                break;

            default:
                DebugUtils.LogError("Invalid movement state: {0}", movementState);
                break;
        }

        m_MovementState = movementState;
    }

    bool SetupHumanPlayer()
    {
        if (LevelManager.Instance.GetPlayer() == null)
        {
            //When new level is loaded all objects are getting destroyed.
            //Setting a flag which prevents object distruction.
            DontDestroyOnLoad(gameObject);

            // Saving our player handle
            LevelManager.Instance.RegisterPlayer(this);

            // Assigning Controlls
            Controller = new MouseKeyPlayerController();

            return true;
        }
        else
        {
            //Do not allow creation of second player object.
            Destroy(gameObject);
            return false;
        }
    }

    // Adding enums for our state Machine
    enum MovementState
    {
        OnGround,
        InAir,
        Disable
    }

    MovementState m_MovementState;

    Rigidbody m_RigidBody;

    Vector3 m_Velocity;

    float m_CenterHeight;

    int m_GroundCheckMask;

    bool m_AllowJump;

}
