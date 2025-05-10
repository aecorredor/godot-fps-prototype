using System;
using System.Diagnostics;
using Godot;

public partial class Player : CharacterBody3D
{
  public enum CharacterPose
  {
    Standing,
    Crouching,
    Proning,
  }

  public static class HeadBobbingSpeed
  {
    public static readonly float Prone = 5.0f;
    public static readonly float CrouchWalk = 10.0f;
    public static readonly float Walk = 14.0f;
    public static readonly float CrouchSprint = 12.0f;
    public static readonly float Sprint = 22.0f;
  }

  public static class HeadBobbingIntensity
  {
    public static readonly float Prone = 0.1f;
    public static readonly float CrouchWalk = 0.15f;
    public static readonly float Walk = 0.2f;
    public static readonly float CrouchSprint = 0.3f;
    public static readonly float Sprint = 0.4f;
  }

  private float lerpModifier = 0.0f;

  // General Movement
  public Vector2 inputDir = Vector2.Zero;
  private const float walkSpeed = 4f;
  private const float sprintSpeed = 8.5f;
  private const float jumpVelocity = 3.5f;
  private const float mouseSensitivity = 0.25f;
  private const float lerpSpeed = 10.0f;
  private const float freeLookTiltAmount = 8.0f;
  private const float crouchDepth = 0.5f;
  private const float crouchSprintSpeed = 4.0f;
  private const float crouchWalkSpeed = 2.0f;
  private const float proneDepth = 1.2f;
  private const float proneSpeed = 1.0f;
  private bool isFreeLooking = false;
  private Vector2 headBobbingVector = Vector2.Zero;
  private float headBobbingIndex = 0.0f;
  private float headBobbingCurrentIntensity = 0.0f;
  private CharacterPose characterCurrentPose = CharacterPose.Standing;
  private CharacterPose characterPrevPose = CharacterPose.Standing;

  // Vectors
  private float currentSpeed = walkSpeed;
  private Vector3 direction = Vector3.Zero;
  private Vector3 lastVelocity = Vector3.Zero;
  private Vector2 currentVelocity = Vector2.Zero;

  // Camera Settings
  private Node3D body;
  private Node3D neck;
  private Node3D head;
  private Node3D eyes;
  private Camera3D camera3D;
  private float accRotationX = 0.0f;
  private float accRotationY = 0.0f;

  [ExportCategory("Collision")]
  [Export]
  private bool DebugCollision = true;

  [ExportCategory("Audio")]
  [Export]
  private float footstepMasterVolume = 0.1f;

  [ExportCategory("Camera")]
  [Export]
  private int freeLookMaxAngle = 55;

  [Export]
  public string locomotionBlendPath { get; set; }

  [Export]
  public float transitionSpeed { get; set; } = 0.1f;

  private const float MAX_STEP_HEIGHT = 0.5f;

  private CollisionShape3D standingCollisionShape;
  private CollisionShape3D crouchingCollisionShape;
  private CollisionShape3D proneCollisionShape;
  private RayCast3D standingRayCast;
  private RayCast3D crouchingRayCast;
  private RayCast3D proningRayCastFront;
  private RayCast3D proningRayCastBack;
  private RayCast3D stairsRayCastAhead;
  private RayCast3D stairsRayCastBelow;

  // Footsteps
  private AudioStreamPlayer3D footstepsAudioPlayer;
  private float lastFootstepTime = 0.0f;
  private bool rightFoot = true;
  private AudioStreamRandomizer walkSoundRandomizer;
  private AudioStreamRandomizer crouchWalkSoundRandomizer;

  // Animations
  private AnimationPlayer cameraAnimationPlayer;
  private AnimationTree characterAnimationTree { get; set; }

  private GodotObject lineDrawer;

  private float lastHeadBobbingYValue = 0.0f;

  // Add this field to track sprint state
  private bool isCurrentlySprinting = false;

  private void DebugRayCast(RayCast3D rayCast, int time = 5)
  {
    if (DebugCollision)
    {
      lineDrawer.Call(
        "DrawLine",
        rayCast.GlobalTransform.Origin,
        rayCast.ToGlobal(rayCast.TargetPosition),
        new Color(0, 0, 1),
        time
      );
    }
  }

  // Simulates the character body moving along the given motion vector to
  // determine whether the body would collide with anything. Mainly used to
  // handle stairs/ramps.
  private bool RunBodyTestMotion(
    Transform3D from,
    Vector3 motion,
    PhysicsTestMotionResult3D result = null
  )
  {
    if (result == null)
    {
      result = new PhysicsTestMotionResult3D();
    }

    var parameters = new PhysicsTestMotionParameters3D()
    {
      From = from,
      Motion = motion,
    };

    return PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result);
  }

  private void SetCharPose(CharacterPose newPose)
  {
    characterPrevPose = characterCurrentPose;
    characterCurrentPose = newPose;
  }

  private void HandleCrouch()
  {
    switch (characterCurrentPose)
    {
      case CharacterPose.Standing:
        SetCharPose(CharacterPose.Crouching);
        break;
      case CharacterPose.Crouching:
        if (!standingRayCast.IsColliding())
        {
          SetCharPose(CharacterPose.Standing);
        }
        break;
      case CharacterPose.Proning:
        if (!crouchingRayCast.IsColliding())
        {
          SetCharPose(CharacterPose.Crouching);
        }
        break;
    }
  }

  private void HandleProne()
  {
    switch (characterCurrentPose)
    {
      case CharacterPose.Proning:
        if (!standingRayCast.IsColliding())
        {
          SetCharPose(CharacterPose.Standing);
        }
        break;

      default:
        var canProne =
          !proningRayCastFront.IsColliding()
          && !proningRayCastBack.IsColliding();
        if (canProne)
        {
          SetCharPose(CharacterPose.Proning);
        }
        break;
    }
  }

  private void HandleJump()
  {
    if (standingRayCast.IsColliding())
    {
      return;
    }

    SetCharPose(CharacterPose.Standing);
    Velocity = Velocity with { Y = jumpVelocity };

    if (!isFreeLooking)
    {
      cameraAnimationPlayer.Play("jump");
    }
  }

  private void HandleFloorActions()
  {
    if (Input.IsActionJustPressed("crouch"))
    {
      HandleCrouch();
    }

    if (Input.IsActionJustPressed("prone"))
    {
      HandleProne();
    }

    if (Input.IsActionJustPressed("jump"))
    {
      HandleJump();
    }
  }

  private void HandleDefaultLook(InputEventMouseMotion eventMouseMotion)
  {
    Transform3D transform = Transform;
    transform.Basis = Basis.Identity;
    Transform = transform;

    accRotationX += eventMouseMotion.Relative.X * mouseSensitivity * 0.01f;
    accRotationY += eventMouseMotion.Relative.Y * mouseSensitivity * 0.01f;
    accRotationY = Mathf.Clamp(accRotationY, -Mathf.Pi / 2, Mathf.Pi / 2);

    RotateObjectLocal(Vector3.Up, -accRotationX);
    // Enables freely looking up and down without moving the whole character
    // mesh.
    neck.Rotation = neck.Rotation with
    {
      X = Mathf.Clamp(
        neck.Rotation.X
          - Mathf.DegToRad(eventMouseMotion.Relative.Y * mouseSensitivity),
        -Mathf.DegToRad(60),
        Mathf.DegToRad(55)
      ),
    };
  }

  private void HandleFreeLook(InputEventMouseMotion eventMouseMotion)
  {
    neck.RotateY(
      -Mathf.DegToRad(eventMouseMotion.Relative.X * mouseSensitivity)
    );
    neck.Rotation = neck.Rotation with
    {
      Y = Mathf.Clamp(
        neck.Rotation.Y,
        -Mathf.DegToRad(freeLookMaxAngle),
        Mathf.DegToRad(freeLookMaxAngle)
      ),
      X = Mathf.Clamp(
        neck.Rotation.X
          - Mathf.DegToRad(eventMouseMotion.Relative.Y * mouseSensitivity),
        -Mathf.DegToRad(60),
        Mathf.DegToRad(55)
      ),
    };
  }

  private void ProcessPose()
  {
    switch (characterCurrentPose)
    {
      case CharacterPose.Proning:
        body.Position = body.Position with
        {
          Y = Mathf.Lerp(body.Position.Y, -proneDepth, lerpModifier),
        };
        proneCollisionShape.Disabled = false;
        crouchingCollisionShape.Disabled = true;
        standingCollisionShape.Disabled = true;
        break;
      case CharacterPose.Crouching:
        body.Position = body.Position with
        {
          Y = Mathf.Lerp(body.Position.Y, -crouchDepth, lerpModifier),
        };
        crouchingCollisionShape.Disabled = false;
        proneCollisionShape.Disabled = true;
        standingCollisionShape.Disabled = true;
        break;
      case CharacterPose.Standing:
        body.Position = body.Position with
        {
          Y = Mathf.Lerp(body.Position.Y, 0.0f, lerpModifier),
        };
        standingCollisionShape.Disabled = false;
        proneCollisionShape.Disabled = true;
        crouchingCollisionShape.Disabled = true;
        break;
    }
  }

  private void ProcessSprint(CharacterPose pose, float delta)
  {
    switch (pose)
    {
      case CharacterPose.Crouching:
        currentSpeed = crouchSprintSpeed;
        headBobbingCurrentIntensity = HeadBobbingIntensity.CrouchSprint;
        headBobbingIndex += HeadBobbingSpeed.CrouchSprint * delta;
        break;

      case CharacterPose.Standing:
        currentSpeed = sprintSpeed;
        headBobbingCurrentIntensity = HeadBobbingIntensity.Sprint;
        headBobbingIndex += HeadBobbingSpeed.Sprint * delta;

        break;
    }
  }

  private void ProcessWalk(CharacterPose pose, float delta)
  {
    switch (pose)
    {
      case CharacterPose.Proning:
        currentSpeed = proneSpeed;
        headBobbingCurrentIntensity = HeadBobbingIntensity.Prone;
        headBobbingIndex += HeadBobbingSpeed.Prone * delta;
        break;

      case CharacterPose.Crouching:
        currentSpeed = crouchWalkSpeed;
        headBobbingCurrentIntensity = HeadBobbingIntensity.CrouchWalk;
        headBobbingIndex += HeadBobbingSpeed.CrouchWalk * delta;
        break;

      case CharacterPose.Standing:
        currentSpeed = walkSpeed;
        headBobbingCurrentIntensity = HeadBobbingIntensity.Walk;
        headBobbingIndex += HeadBobbingSpeed.Walk * delta;
        break;
    }
  }

  private void PlayFootstepSound()
  {
    if (!IsOnFloor() || footstepsAudioPlayer == null)
      return;

    var isSprinting = isCurrentlySprinting;

    // Check if enough time has passed since the last footstep
    float currentTime = Time.GetTicksMsec() / 1000.0f;
    float minTimeBetweenSteps = isSprinting ? 0.1f : 0.4f;

    if (currentTime - lastFootstepTime < minTimeBetweenSteps)
    {
      return;
    }

    lastFootstepTime = currentTime;

    // Get the appropriate sound based on movement state
    AudioStreamRandomizer currentRandomizer;
    float volumeScale;

    switch (characterCurrentPose)
    {
      case CharacterPose.Crouching:
        currentRandomizer = crouchWalkSoundRandomizer;
        volumeScale = isSprinting ? 0.3f : 0.15f;
        break;

      default:
        currentRandomizer = walkSoundRandomizer;

        if (isSprinting)
        {
          volumeScale = 1.0f;
        }
        else
        {
          volumeScale = 0.5f;
        }
        break;
    }

    volumeScale *= footstepMasterVolume;

    if (currentRandomizer != null)
    {
      footstepsAudioPlayer.Stream = currentRandomizer;
      footstepsAudioPlayer.VolumeDb = Mathf.LinearToDb(volumeScale);
      footstepsAudioPlayer.PitchScale = rightFoot ? 1.0f : 1.05f;
      footstepsAudioPlayer.Play();
      rightFoot = !rightFoot;
    }
  }

  private void ActivateHeadBobbing(float lerpModifier)
  {
    // Store previous value before calculating new one
    lastHeadBobbingYValue = headBobbingVector.Y;

    headBobbingVector.Y = Mathf.Sin(headBobbingIndex);
    headBobbingVector.X = Mathf.Sin(headBobbingIndex / 2) + 0.5f;
    eyes.Position = eyes.Position with
    {
      X = Mathf.Lerp(
        eyes.Position.X,
        headBobbingVector.X * headBobbingCurrentIntensity,
        lerpModifier
      ),
      Y = Mathf.Lerp(
        eyes.Position.Y,
        headBobbingVector.Y * (headBobbingCurrentIntensity / 2),
        lerpModifier
      ),
    };

    // More reliable zero-crossing detection that uses the previous and current
    // values This catches the zero-crossing even when frames skip the exact 0
    // value
    if (lastHeadBobbingYValue <= 0 && headBobbingVector.Y > 0)
    {
      PlayFootstepSound();
    }

    // Reset the index periodically to avoid floating-point precision issues
    // 2π ≈ 6.28, so we reset after a few complete cycles
    if (headBobbingIndex > 100.0f)
    {
      // Reset to a value that maintains the same position in the cycle
      headBobbingIndex = headBobbingIndex % (2 * Mathf.Pi);
    }
  }

  private void ResetEyesPosition(float lerpModifier)
  {
    eyes.Position = eyes.Position with
    {
      X = Mathf.Lerp(eyes.Position.X, 0.0f, lerpModifier),
      Y = Mathf.Lerp(eyes.Position.Y, 0.0f, lerpModifier),
    };

    // Reset footstep timing when not moving
    lastFootstepTime = 0.0f;
  }

  private bool CanStartSprint()
  {
    if (
      !Input.IsActionPressed("sprint")
      || characterCurrentPose == CharacterPose.Proning
      || inputDir == Vector2.Zero
    )
    {
      return false;
    }

    // Convert input direction to a normalized vector
    Vector2 normalizedInput = inputDir.Normalized();

    // Forward is (0, -1) in our input space
    Vector2 forward = new Vector2(0, -1);

    // Calculate angle between input and forward direction
    float angle = Mathf.Abs(forward.AngleTo(normalizedInput));

    // Only allow sprint if we're within 30 degrees of forward
    return angle <= Mathf.DegToRad(30);
  }

  private bool CanContinueSprint()
  {
    return Input.IsActionPressed("sprint")
      && inputDir != Vector2.Zero
      && characterCurrentPose != CharacterPose.Proning;
  }

  private Vector2 AdjustInputForSprinting(Vector2 input)
  {
    if (!isCurrentlySprinting)
      return input;

    // Create a modified input with reduced side movement
    // 0.1f means side movement is at 10% effectiveness while sprinting
    return new Vector2(input.X * 0.2f, input.Y);
  }

  private void ProcessMovement(float delta, Vector2 inputDir)
  {
    if (IsOnFloor())
    {
      isCurrentlySprinting = isCurrentlySprinting
        ? CanContinueSprint()
        : CanStartSprint();

      // Apply side movement reduction when sprinting
      Vector2 adjustedInput = AdjustInputForSprinting(inputDir);

      if (isCurrentlySprinting)
      {
        ProcessSprint(characterCurrentPose, delta);
      }
      else
      {
        ProcessWalk(characterCurrentPose, delta);
      }

      if (adjustedInput != Vector2.Zero)
      {
        ActivateHeadBobbing(lerpModifier);
      }
      else
      {
        ResetEyesPosition(lerpModifier);
      }

      ProcessFloorActions(lerpModifier, adjustedInput);
    }
    else
    {
      isCurrentlySprinting = false;
      AddGravity(delta);
    }
  }

  private void ProcessFreeLook()
  {
    // This action is continuous, so it needs to be checked every frame, and
    // not only when the action is pressed.
    if (isFreeLooking)
    {
      // Cancel the camera animation for jumping/landing when free looking.
      // The free look needs a different camera animation which is not
      // currently present.
      cameraAnimationPlayer.Advance(2);
      camera3D.Rotation = camera3D.Rotation with
      {
        Z = -Mathf.DegToRad(neck.Rotation.Y * freeLookTiltAmount),
      };
    }
    else
    {
      neck.Rotation = neck.Rotation with
      {
        Y = Mathf.Lerp(neck.Rotation.Y, 0.0f, lerpModifier),
      };
      camera3D.Rotation = camera3D.Rotation with
      {
        Z = Mathf.Lerp(camera3D.Rotation.Z, 0.0f, lerpModifier),
      };
    }
  }

  private void ProcessFloorActions(float lerpModifier, Vector2 inputDir)
  {
    // Get the input direction and handle the movement/deceleration.
    // Don't let the player change directions while in the air.
    direction = direction.Lerp(
      (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized(),
      lerpModifier
    );

    if (lastVelocity.Y < -2.0f && !isFreeLooking)
    {
      cameraAnimationPlayer.Play("land");
    }
  }

  private void AddGravity(double delta)
  {
    // Add the gravity.
    Velocity = Velocity with
    {
      Y =
        Velocity.Y
        - (
          (float)ProjectSettings.GetSetting("physics/3d/default_gravity")
          * (float)delta
        ),
    };
  }

  private bool IsSurfaceTooSteep(Vector3 normal)
  {
    return normal.AngleTo(Vector3.Up) > FloorMaxAngle;
  }

  private bool SnapUpStairs(float delta)
  {
    if (!IsOnFloor())
    {
      return false;
    }

    var expectedMotion = lastVelocity * new Vector3(1, 0, 1) * delta;
    var nextStepPosition = GlobalTransform.Translated(
      expectedMotion + new Vector3(0, MAX_STEP_HEIGHT, 0)
    );
    var stepDownCheck = new KinematicCollision3D();

    if (
      TestMove(
        nextStepPosition,
        new Vector3(0, -MAX_STEP_HEIGHT, 0),
        stepDownCheck
      )
    )
    {
      var stepHeight = (
        nextStepPosition.Origin + stepDownCheck.GetTravel() - GlobalPosition
      ).Y;

      if (
        stepHeight > MAX_STEP_HEIGHT
        || stepHeight < 0.01f
        || (stepDownCheck.GetPosition() - GlobalPosition).Y > MAX_STEP_HEIGHT
      )
        return false;

      stairsRayCastAhead.GlobalPosition =
        stepDownCheck.GetPosition()
        + new Vector3(0, MAX_STEP_HEIGHT * 0.8f, 0)
        + expectedMotion.Normalized();
      stairsRayCastAhead.ForceRaycastUpdate();

      DebugRayCast(stairsRayCastAhead);

      if (
        stairsRayCastAhead.IsColliding()
        && !IsSurfaceTooSteep(stairsRayCastAhead.GetCollisionNormal())
      )
      {
        GlobalPosition = nextStepPosition.Origin + stepDownCheck.GetTravel();
        ApplyFloorSnap();

        return true;
      }
    }

    return false;
  }

  private void SnapDownStairs()
  {
    // This thing is using a raycast on top of a test move because the
    // test move sometimes is not perfect because of the collision shape of
    // the player. The capsule can sometimes hit the edge of a stair and not
    // return correct results.
    stairsRayCastBelow.ForceRaycastUpdate();
    var stairsAreBelow =
      stairsRayCastBelow.IsColliding()
      && (!IsSurfaceTooSteep(stairsRayCastBelow.GetCollisionNormal()));

    DebugRayCast(stairsRayCastBelow);

    if (
      !IsOnFloor()
      && stairsAreBelow
      // Don't snap when we're jumping/actually falling.
      && lastVelocity.Y < 0
      && lastVelocity.Y > -0.5f
    )
    {
      var stepDownCheck = new KinematicCollision3D();

      if (
        TestMove(
          GlobalTransform,
          new Vector3(0, -MAX_STEP_HEIGHT, 0),
          stepDownCheck
        )
      )
      {
        Position = Position with
        {
          Y = Position.Y + stepDownCheck.GetTravel().Y,
        };
        ApplyFloorSnap();
      }
    }
  }

  public override void _Ready()
  {
    Input.MouseMode = Input.MouseModeEnum.Captured;
    body = GetNode<Node3D>("body");
    neck = GetNode<Node3D>("body/neck");
    head = neck.GetNode<Node3D>("head");
    eyes = head.GetNode<Node3D>("eyes");
    camera3D = eyes.GetNode<Camera3D>("Camera3D");
    standingCollisionShape = GetNode<CollisionShape3D>(
      "standing_collision_shape"
    );
    crouchingCollisionShape = GetNode<CollisionShape3D>(
      "crouching_collision_shape"
    );
    proneCollisionShape = GetNode<CollisionShape3D>("prone_collision_shape");
    standingRayCast = GetNode<RayCast3D>("standing_ray_cast");
    crouchingRayCast = GetNode<RayCast3D>("crouching_ray_cast");
    proningRayCastFront = GetNode<RayCast3D>("proning_ray_cast_front");
    proningRayCastBack = GetNode<RayCast3D>("proning_ray_cast_back");
    stairsRayCastAhead = GetNode<RayCast3D>("stairs_ray_cast_ahead");
    stairsRayCastBelow = GetNode<RayCast3D>("stairs_ray_cast_below");
    cameraAnimationPlayer = eyes.GetNode<AnimationPlayer>("AnimationPlayer");
    characterAnimationTree = GetNode<AnimationTree>("Character/AnimationTree");

    // Footstep sound setup
    footstepsAudioPlayer = GetNode<AudioStreamPlayer3D>("FootstepsAudioPlayer");
    walkSoundRandomizer = ResourceLoader.Load<AudioStreamRandomizer>(
      "res://assets/sounds/WalkAudioStreamRandomizer.tres"
    );
    crouchWalkSoundRandomizer = ResourceLoader.Load<AudioStreamRandomizer>(
      "res://assets/sounds/CrouchAudioStreamRandomizer.tres"
    );

    if (DebugCollision)
    {
      GDScript Draw3DLineScript = GD.Load<GDScript>(
        "res://scripts/DrawLine3D.gd"
      );
      lineDrawer = (GodotObject)Draw3DLineScript.New();
      AddChild((Node)lineDrawer);
    }
  }

  public override void _Input(InputEvent @event)
  {
    isFreeLooking = Input.IsActionPressed("free_look");

    if (@event is InputEventMouseMotion eventMouseMotion)
    {
      if (isFreeLooking)
      {
        HandleFreeLook(eventMouseMotion);
      }
      else
      {
        HandleDefaultLook(eventMouseMotion);
      }
    }

    if (IsOnFloor())
    {
      HandleFloorActions();
    }
  }

  public override void _Process(double delta)
  {
    Vector2 newDelta = inputDir - currentVelocity;
    if (newDelta.Length() > transitionSpeed * (float)delta)
    {
      newDelta = newDelta.Normalized() * transitionSpeed * (float)delta;
    }
    currentVelocity += newDelta;

    characterAnimationTree.Set(locomotionBlendPath, currentVelocity);
  }

  public override void _PhysicsProcess(double delta)
  {
    lerpModifier = (float)delta * lerpSpeed;
    inputDir = Input.GetVector("left", "right", "forward", "backward");

    ProcessMovement((float)delta, inputDir);
    ProcessPose();
    ProcessFreeLook();

    if (direction != Vector3.Zero)
    {
      Velocity = Velocity with { X = direction.X * currentSpeed };
      Velocity = Velocity with { Z = direction.Z * currentSpeed };
    }

    lastVelocity = Velocity;

    if (!SnapUpStairs((float)delta))
    {
      MoveAndSlide();
      SnapDownStairs();
    }
  }
}
