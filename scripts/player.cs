using Godot;
using System;

public partial class player : CharacterBody3D
{
	// General Movement
	private const float walkSpeed = 5.0f;
	private const float sprintSpeed = 8.5f;
	private const float jumpVelocity = 4.5f;
	private const float mouseSensitivity = 0.25f;
	private const float lerpSpeed = 10.0f;
	private const float freeLookTiltAmount = 8.0f;
	private const float crouchDepth = 0.5f;
	private const float crouchSprintSpeed = 4.0f;
	private const float crouchWalkSpeed = 2.0f;
	private const float proneDepth = 1.2f;
	private const float proneSpeed = 1.0f;

	public enum CharacterPose
	{
		Standing,
		Crouching,
		Proning
	}

	// Stateful
	private float currentSpeed = walkSpeed;
	private float prevSpeed = walkSpeed;
	private Vector3 direction = Vector3.Zero;
	private bool isJumping = false;
	private bool isFreeLooking = false;
	private Vector2 headBobbingVector = Vector2.Zero;
	private float headBobbingIndex = 0.0f;
	private float headBobbingCurrentIntensity = 0.0f;
	private Vector3 lastVelocity = Vector3.Zero;
	private CharacterPose characterCurrentPose = CharacterPose.Standing;
	private CharacterPose characterPrevPose = CharacterPose.Standing;
	private Node3D neck;
	private Node3D head;
	private Node3D eyes;
	private Camera3D camera3D;
	private CollisionShape3D standingCollisionShape;
	private CollisionShape3D crouchingCollisionShape;
	private CollisionShape3D proneCollisionShape;
	private RayCast3D standingRayCast;
	private RayCast3D crouchingRayCast;
	private RayCast3D proningRayCast;
	private AnimationPlayer animationPlayer;

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
		public static readonly float CrouchWalk = 0.05f;
		public static readonly float Walk = 0.1f;
		public static readonly float CrouchSprint = 0.075f;
		public static readonly float Sprint = 0.2f;
	}

	private void SetCharPose(CharacterPose newPose)
	{
		characterPrevPose = characterCurrentPose;
		characterCurrentPose = newPose;
	}

	private void HandlePoseChange(float lerpModifier)
	{
		float proneHeight = Mathf.Lerp(head.Position.Y, -proneDepth, lerpModifier);
		float crouchHeight = Mathf.Lerp(head.Position.Y, -crouchDepth, lerpModifier);
		float standingHeight = Mathf.Lerp(head.Position.Y, 0.0f, lerpModifier);

		switch (characterCurrentPose)
		{
			case CharacterPose.Proning:
				head.Position = head.Position with { Y = proneHeight };
				proneCollisionShape.Disabled = false;
				crouchingCollisionShape.Disabled = true;
				standingCollisionShape.Disabled = true;
				break;
			case CharacterPose.Crouching:
				head.Position = head.Position with { Y = crouchHeight };
				crouchingCollisionShape.Disabled = false;
				proneCollisionShape.Disabled = true;
				standingCollisionShape.Disabled = true;
				break;
			case CharacterPose.Standing:
				head.Position = head.Position with { Y = standingHeight };
				standingCollisionShape.Disabled = false;
				proneCollisionShape.Disabled = true;
				crouchingCollisionShape.Disabled = true;
				break;
		}
	}

	private void HandleMovement(float delta, float lerpModifier, Vector2 inputDir)
	{
		bool isCrouching = characterCurrentPose == CharacterPose.Crouching;

		if (characterCurrentPose == CharacterPose.Proning)
		{
			currentSpeed = proneSpeed;
			headBobbingCurrentIntensity = HeadBobbingIntensity.Prone;
			headBobbingIndex += HeadBobbingSpeed.Prone * delta;
		}
		else if (Input.IsActionPressed("sprint"))
		{
			if (IsOnFloor())
			{
				currentSpeed = isCrouching ? crouchSprintSpeed : sprintSpeed;
				headBobbingCurrentIntensity = isCrouching ? HeadBobbingIntensity.CrouchSprint : HeadBobbingIntensity.Sprint;
				headBobbingIndex += isCrouching ? HeadBobbingSpeed.CrouchSprint * delta : HeadBobbingSpeed.Sprint * delta;
			}
			else
			{
				// Don't let the player start sprinting while in the air.
				currentSpeed = prevSpeed;
			}
		}
		else
		{
			currentSpeed = isCrouching ? crouchWalkSpeed : walkSpeed;
			headBobbingCurrentIntensity = isCrouching ? HeadBobbingIntensity.CrouchWalk : HeadBobbingIntensity.Walk;
			headBobbingIndex += isCrouching ? HeadBobbingSpeed.CrouchWalk * delta : HeadBobbingSpeed.Walk * delta;
		}

		if (IsOnFloor() && inputDir != Vector2.Zero)
		{
			headBobbingVector.Y = Mathf.Sin(headBobbingIndex);
			headBobbingVector.X = Mathf.Sin(headBobbingIndex / 2) + 0.5f;
			eyes.Position = eyes.Position with
			{
				X = Mathf.Lerp(eyes.Position.X, headBobbingVector.X * headBobbingCurrentIntensity, lerpModifier),
				Y = Mathf.Lerp(eyes.Position.Y, headBobbingVector.Y * (headBobbingCurrentIntensity / 2), lerpModifier)
			};
		}
		else
		{
			eyes.Position = eyes.Position with
			{
				X = Mathf.Lerp(eyes.Position.X, 0.0f, lerpModifier),
				Y = Mathf.Lerp(eyes.Position.Y, 0.0f, lerpModifier)
			};

			prevSpeed = currentSpeed;
			if (!IsOnFloor())
			{
				currentSpeed = currentSpeed - 2.0f;
			}
			else
			{
				currentSpeed = prevSpeed;
			}
		}
	}

	private void HandleFreeLook(float lerpModifier)
	{
		if (Input.IsActionPressed("free_look"))
		{
			isFreeLooking = true;
			camera3D.Rotation = camera3D.Rotation with { Z = -Mathf.DegToRad(neck.Rotation.Y * freeLookTiltAmount) };
		}
		else
		{
			if (isFreeLooking)
			{
				head.Rotation = head.Rotation with { X = Mathf.Lerp(head.Rotation.X, 0.0f, lerpModifier) };
			}

			isFreeLooking = false;
			neck.Rotation = neck.Rotation with { Y = Mathf.Lerp(neck.Rotation.Y, 0.0f, lerpModifier) };
			camera3D.Rotation = camera3D.Rotation with { Z = Mathf.Lerp(camera3D.Rotation.Z, 0.0f, lerpModifier) };
		}
	}

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;
		neck = GetNode<Node3D>("neck");
		head = neck.GetNode<Node3D>("head");
		eyes = head.GetNode<Node3D>("eyes");
		camera3D = eyes.GetNode<Camera3D>("Camera3D");
		standingCollisionShape = GetNode<CollisionShape3D>("standing_collision_shape");
		crouchingCollisionShape = GetNode<CollisionShape3D>("crouching_collision_shape");
		proneCollisionShape = GetNode<CollisionShape3D>("prone_collision_shape");
		standingRayCast = GetNode<RayCast3D>("standing_ray_cast");
		crouchingRayCast = GetNode<RayCast3D>("crouching_ray_cast");
		proningRayCast = GetNode<RayCast3D>("proning_ray_cast");
		animationPlayer = eyes.GetNode<AnimationPlayer>("AnimationPlayer");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion eventMouseMotion)
		{
			float mainRotation = -Mathf.DegToRad(eventMouseMotion.Relative.X * mouseSensitivity);
			if (isFreeLooking)
			{
				neck.RotateY(mainRotation);
				neck.Rotation = neck.Rotation with { Y = Mathf.Clamp(neck.Rotation.Y, -Mathf.DegToRad(115), Mathf.DegToRad(115)) };
			}
			else
			{
				// This changes the player direction. (rotates the actual body).
				RotateY(mainRotation);
			}

			// This rotates the player camera view.
			head.RotateX(-Mathf.DegToRad(eventMouseMotion.Relative.Y * mouseSensitivity));
			head.Rotation = head.Rotation with { X = Mathf.Clamp(head.Rotation.X, -Mathf.DegToRad(75), Mathf.DegToRad(75)) };
		}

		if (Input.IsActionJustPressed("crouch") && IsOnFloor())
		{
			GD.Print("Crouch");
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

		if (Input.IsActionJustPressed("prone") && IsOnFloor())
		{
			switch (characterCurrentPose)
			{
				case CharacterPose.Proning:
					if (characterPrevPose == CharacterPose.Crouching && !crouchingRayCast.IsColliding())
					{
						SetCharPose(CharacterPose.Crouching);
					}
					else if (!standingRayCast.IsColliding())
					{
						SetCharPose(CharacterPose.Standing);
					}
					else if (!crouchingRayCast.IsColliding())
					{
						SetCharPose(CharacterPose.Crouching);
					}
					break;
				default:
					if (!proningRayCast.IsColliding())
					{
						SetCharPose(CharacterPose.Proning);
					}
					break;
			}
		}

		if (Input.IsActionJustPressed("ui_accept") && !standingRayCast.IsColliding() && IsOnFloor())
		{
			SetCharPose(CharacterPose.Standing);
			Velocity = Velocity with { Y = jumpVelocity };
			animationPlayer.Play("jump");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float lerpModifier = (float)delta * lerpSpeed;
		Vector2 inputDir = Input.GetVector("left", "right", "forward", "backward");

		// Add the gravity.
		if (!IsOnFloor())
		{
			Velocity = Velocity with { Y = Velocity.Y - ((float)ProjectSettings.GetSetting("physics/3d/default_gravity") * (float)delta) };
		}

		HandleMovement((float)delta, lerpModifier, inputDir);
		HandlePoseChange(lerpModifier);
		HandleFreeLook(lerpModifier);

		// Get the input direction and handle the movement/deceleration.
		// Don't let the player change directions while in the air.
		if (IsOnFloor())
		{
			direction = direction.Lerp((Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized(), lerpModifier);

			if (lastVelocity.Y < 0.0f)
			{
				animationPlayer.Play("land");
			}
		}

		if (direction != Vector3.Zero)
		{
			Velocity = Velocity with { X = direction.X * currentSpeed };
			Velocity = Velocity with { Z = direction.Z * currentSpeed };
		}
		else
		{
			Velocity = Velocity with { X = Mathf.MoveToward(Velocity.X, 0, currentSpeed) };
			Velocity = Velocity with { Z = Mathf.MoveToward(Velocity.Z, 0, currentSpeed) };
		}

		lastVelocity = Velocity;
		MoveAndSlide();
	}
}
