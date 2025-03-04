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

  // General Movement
  public Vector2 inputDir = Vector2.Zero;
  private const float walkSpeed = 2.5f;
  private const float sprintSpeed = 5.0f;
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

  // Camera Settings
  private Node3D neck;
  private Node3D head;
  private Node3D eyes;
  private Camera3D camera3D;
  private float accRotationX = 0.0f;
  private float accRotationY = 0.0f;

  [ExportCategory("Collision")]
  [Export]
  private bool DebugCollision = true;

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

  private AnimationPlayer animationPlayer;

  private GodotObject lineDrawer;

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
	  animationPlayer.Play("jump");
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
	RotateObjectLocal(Vector3.Right, -accRotationY);
	Rotation = Rotation with
	{
	  X = Mathf.Clamp(Rotation.X, -Mathf.DegToRad(60), Mathf.DegToRad(55)),
	};
  }

  private void HandleFreeLook(InputEventMouseMotion eventMouseMotion)
  {
	neck.RotateY(
	  -Mathf.DegToRad(eventMouseMotion.Relative.X * mouseSensitivity)
	);
	neck.Rotation = neck.Rotation with
	{
	  Y = Mathf.Clamp(neck.Rotation.Y, -Mathf.DegToRad(95), Mathf.DegToRad(95)),
	};
  }

  private void ProcessPose(float lerpModifier)
  {
	switch (characterCurrentPose)
	{
	  case CharacterPose.Proning:
		head.Position = head.Position with
		{
		  Y = Mathf.Lerp(head.Position.Y, -proneDepth, lerpModifier),
		};
		proneCollisionShape.Disabled = false;
		crouchingCollisionShape.Disabled = true;
		standingCollisionShape.Disabled = true;
		break;
	  case CharacterPose.Crouching:
		head.Position = head.Position with
		{
		  Y = Mathf.Lerp(head.Position.Y, -crouchDepth, lerpModifier),
		};
		crouchingCollisionShape.Disabled = false;
		proneCollisionShape.Disabled = true;
		standingCollisionShape.Disabled = true;
		break;
	  case CharacterPose.Standing:
		head.Position = head.Position with
		{
		  Y = Mathf.Lerp(head.Position.Y, 0.0f, lerpModifier),
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

  private void ActivateHeadBobbing(float lerpModifier)
  {
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
  }

  private void ResetEyesPosition(float lerpModifier)
  {
	eyes.Position = eyes.Position with
	{
	  X = Mathf.Lerp(eyes.Position.X, 0.0f, lerpModifier),
	  Y = Mathf.Lerp(eyes.Position.Y, 0.0f, lerpModifier),
	};
  }

  private void ProcessMovement(
	float delta,
	float lerpModifier,
	Vector2 inputDir
  )
  {
	if (IsOnFloor())
	{
	  if (Input.IsActionPressed("sprint"))
	  {
		ProcessSprint(characterCurrentPose, delta);
	  }
	  else
	  {
		ProcessWalk(characterCurrentPose, delta);
	  }

	  if (inputDir != Vector2.Zero)
	  {
		ActivateHeadBobbing(lerpModifier);
	  }
	  else
	  {
		ResetEyesPosition(lerpModifier);
	  }
	}
  }

  private void ProcessFreeLook(float lerpModifier)
  {
	// This action is continuous, so it needs to be checked every frame, and
	// not only when the action is pressed.
	if (isFreeLooking)
	{
	  // Cancel the camera animation for jumping/landing when free looking.
	  // The free look needs a different camera animation which is not
	  // currently present.
	  animationPlayer.Advance(2);
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
	  animationPlayer.Play("land");
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
	neck = GetNode<Node3D>("neck");
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
	animationPlayer = eyes.GetNode<AnimationPlayer>("AnimationPlayer");

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

  public override void _PhysicsProcess(double delta)
  {
	float lerpModifier = (float)delta * lerpSpeed;
	inputDir = Input.GetVector("left", "right", "forward", "backward");

	if (IsOnFloor())
	{
	  ProcessFloorActions(lerpModifier, inputDir);
	}
	else
	{
	  AddGravity(delta);
	}

	ProcessMovement((float)delta, lerpModifier, inputDir);
	ProcessPose(lerpModifier);
	ProcessFreeLook(lerpModifier);

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
