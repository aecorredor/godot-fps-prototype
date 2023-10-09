extends CharacterBody3D

# General Movement
const walk_speed = 5.0
const sprint_speed = 8.5
const jump_velocity = 4.5
const mouse_sensitivity = 0.25
const lerp_speed = 10.0
const free_look_tilt_amount = 8
const crouch_depth = 0.5
const crouch_sprint_speed = 4.0
const crouch_walk_speed = 2.0
const prone_depth = 1.2
const prone_speed = 1.0

enum CharacterPose {
	Standing,
	Crouching,
	Proning
}

var HeadBobbingSpeed = {
	prone = 5.0,
	crouch_walk = 10.0,
	walk = 14.0,
	crouch_sprint = 12.0,
	sprint = 22.0
}

var HeadBobbingIntensity = {
	prone = 0.1,
	crouch_walk = 0.05,
	walk = 0.1,
	crouch_sprint = 0.075,
	sprint = 0.2
}

# Stateful
var current_speed = walk_speed
var prev_speed = current_speed
var direction = Vector3.ZERO
var is_jumping = false
var is_free_looking = false
var head_bobbing_vector = Vector2.ZERO
var head_bobbing_index = 0.0
var head_bobbing_current_intensity = 0.0
var last_velocity = Vector3.ZERO
var character_current_pose: CharacterPose = CharacterPose.Standing
var character_prev_pose: CharacterPose = character_current_pose

@onready var neck = $neck
@onready var head = $neck/head
@onready var eyes = $neck/head/eyes
@onready var camera_3d = $neck/head/eyes/Camera3D
@onready var standing_collision_shape = $standing_collision_shape
@onready var crouching_collision_shape = $crouching_collision_shape
@onready var prone_collision_shape = $prone_collision_shape
@onready var standing_ray_cast = $standing_ray_cast
@onready var crouching_ray_cast = $crouching_ray_cast
@onready var proning_ray_cast = $proning_ray_cast
@onready var animation_player = $neck/head/eyes/AnimationPlayer

# Get the gravity from the project settings to be synced with RigidBody nodes.
var gravity = ProjectSettings.get_setting("physics/3d/default_gravity")

func set_char_pose(new_pose: CharacterPose):
	character_prev_pose = character_current_pose
	character_current_pose = new_pose

func handle_pose_change(lerp_modifier: float):
	var prone_height = lerp(head.position.y, -prone_depth, lerp_modifier)
	var crouch_height = lerp(head.position.y, -crouch_depth, lerp_modifier)
	var standing_height = lerp(head.position.y, 0.0, lerp_modifier)

	match character_current_pose:
		CharacterPose.Proning:
			head.position.y = prone_height
			prone_collision_shape.disabled = false
			crouching_collision_shape.disabled = true
			standing_collision_shape.disabled = true
		CharacterPose.Crouching:
			head.position.y = crouch_height
			crouching_collision_shape.disabled = false
			prone_collision_shape.disabled = true
			standing_collision_shape.disabled = true
		CharacterPose.Standing:
			head.position.y = standing_height
			standing_collision_shape.disabled = false
			prone_collision_shape.disabled = true
			crouching_collision_shape.disabled = true

func handle_movement(delta: float, lerp_modifier: float, input_dir: Vector2):
	var is_crouching = character_current_pose == CharacterPose.Crouching
	
	if (character_current_pose == CharacterPose.Proning):
		current_speed = prone_speed
		head_bobbing_current_intensity = HeadBobbingIntensity.prone 
		head_bobbing_index += HeadBobbingSpeed.prone * delta 
	elif (Input.is_action_pressed("sprint")):
		if (is_on_floor()):
			current_speed = crouch_sprint_speed if is_crouching else sprint_speed
			head_bobbing_current_intensity = HeadBobbingIntensity.crouch_sprint if is_crouching else HeadBobbingIntensity.sprint
			head_bobbing_index += HeadBobbingSpeed.crouch_sprint * delta if is_crouching else HeadBobbingSpeed.sprint * delta
		else: 
			# Don't let the player start sprinting while in the air.
			current_speed = prev_speed
	else:
		current_speed = crouch_walk_speed if is_crouching else walk_speed	
		head_bobbing_current_intensity = HeadBobbingIntensity.crouch_walk if is_crouching else HeadBobbingIntensity.walk
		head_bobbing_index += HeadBobbingSpeed.crouch_walk * delta if is_crouching else HeadBobbingSpeed.walk * delta
		
	if (is_on_floor() && input_dir != Vector2.ZERO):
		head_bobbing_vector.y = sin(head_bobbing_index)
		head_bobbing_vector.x = sin(head_bobbing_index / 2) + 0.5
		eyes.position.y = lerp(eyes.position.y, head_bobbing_vector.y * (head_bobbing_current_intensity / 2), lerp_modifier)
		eyes.position.x = lerp(eyes.position.x, head_bobbing_vector.x * head_bobbing_current_intensity, lerp_modifier)
	else:
		eyes.position.y = lerp(eyes.position.y, 0.0, lerp_modifier)
		eyes.position.x = lerp(eyes.position.x, 0.0, lerp_modifier)
		
	prev_speed = current_speed
	if (!is_on_floor()):
		current_speed = current_speed - 2.0
	else:
		current_speed = prev_speed

		
func handle_free_look(lerp_modifier: float):
	if (Input.is_action_pressed("free_look")):
		is_free_looking = true
		camera_3d.rotation.z = -deg_to_rad(neck.rotation.y * free_look_tilt_amount)
	else:
		if (is_free_looking):
			head.rotation.x = lerp(head.rotation.x, 0.0, lerp_modifier)
			
		is_free_looking = false
		neck.rotation.y = lerp(neck.rotation.y, 0.0, lerp_modifier)
		camera_3d.rotation.z = lerp(camera_3d.rotation.z, 0.0, lerp_modifier)

func _ready():
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)

func _input(event):
	if event is InputEventMouseMotion:
		var main_rotation = deg_to_rad(-event.relative.x * mouse_sensitivity)
		if (is_free_looking):  
			neck.rotate_y(main_rotation)
			neck.rotation.y = clamp(neck.rotation.y, deg_to_rad(-115), deg_to_rad(115))
		else:
			# This changes the player direction. (rotates the actual body).
			rotate_y(main_rotation)
		
		# This rotates the player camera view.
		head.rotate_x(deg_to_rad(-event.relative.y * mouse_sensitivity))
		head.rotation.x = clamp(head.rotation.x, deg_to_rad(-75), deg_to_rad(75))
		
	if (Input.is_action_just_pressed("crouch") && is_on_floor()):
		match character_current_pose:
			CharacterPose.Standing:
				set_char_pose(CharacterPose.Crouching)
			CharacterPose.Crouching:
				if (!standing_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Standing)
			CharacterPose.Proning:
				if (!crouching_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Crouching)
		
	if (Input.is_action_just_pressed("prone") && is_on_floor()):
		match character_current_pose:
			CharacterPose.Proning:
				if (character_prev_pose == CharacterPose.Crouching && !crouching_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Crouching)
				elif (!standing_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Standing)
				elif (!crouching_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Crouching)
			_:
				if (!proning_ray_cast.is_colliding()):
					set_char_pose(CharacterPose.Proning)

	if (Input.is_action_just_pressed("ui_accept") && !standing_ray_cast.is_colliding() && is_on_floor()):
		set_char_pose(CharacterPose.Standing)
		velocity.y = jump_velocity
		animation_player.play("jump")

		
func _physics_process(delta):
	var lerp_modifier = delta * lerp_speed
	var input_dir = Input.get_vector("left", "right", "forward", "backward")
	
	# Add the gravity.
	if not is_on_floor():
		velocity.y -= gravity * delta
	
	handle_movement(delta, lerp_modifier, input_dir)	
	handle_pose_change(lerp_modifier)
	handle_free_look(lerp_modifier)
	
	# Get the input direction and handle the movement/deceleration.
	# Don't let the player change directions while in the air.
	if (is_on_floor()):
		direction = lerp(direction, (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized(), lerp_modifier)
		if last_velocity.y < 0.0:
			animation_player.play('land')
			
	if direction:
		velocity.x = direction.x * current_speed
		velocity.z = direction.z * current_speed
	else:
		velocity.x = move_toward(velocity.x, 0, current_speed)
		velocity.z = move_toward(velocity.z, 0, current_speed)

	last_velocity = velocity
	move_and_slide()
