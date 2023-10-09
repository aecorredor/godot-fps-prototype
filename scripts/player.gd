extends CharacterBody3D

# General Movement
const walk_speed = 5.0
const sprint_speed = 8.5
const jump_velocity = 4.5
const mouse_sensitivity = 0.25
const lerp_speed = 10.0
const free_look_tilt_amount = 8

# Crouching
const crouch_depth = 0.5
const crouch_sprint_speed = 4.0
const crouch_walk_speed = 2.0

# Head bobbing
const head_bobbing_walk_speed = 14.0
const head_bobbing_sprint_speed = 22.0
const head_bobbing_crouch_walk_speed = 10.0
const head_bobbing_crouch_sprint_speed = 12.0
const head_bobbing_walk_intensity = 0.1
const head_bobbing_sprint_intensity = 0.2
const head_bobbing_crouch_walk_intensity = 0.05
const head_bobbing_crouch_sprint_intensity = 0.075

# Stateful
var current_speed = walk_speed
var prev_speed = current_speed
var direction = Vector3.ZERO
var is_crouching = false
var is_jumping = false
var is_free_looking = false
var head_bobbing_vector = Vector2.ZERO
var head_bobbing_index = 0.0
var head_bobbing_current_intensity = 0.0

@onready var neck = $neck
@onready var head = $neck/head
@onready var eyes = $neck/head/eyes
@onready var camera_3d = $neck/head/eyes/Camera3D
@onready var standing_collision_shape = $standing_collision_shape
@onready var crouching_collision_shape = $crouching_collision_shape
@onready var ray_cast_3d = $RayCast3D

# Get the gravity from the project settings to be synced with RigidBody nodes.
var gravity = ProjectSettings.get_setting("physics/3d/default_gravity")

func handle_crouch(lerp_modifier: float): 
	var crouch_height = lerp(head.position.y, -crouch_depth, lerp_modifier)
	var standing_height = lerp(head.position.y, 0.0, lerp_modifier)
	head.position.y = crouch_height  if is_crouching else standing_height
	standing_collision_shape.disabled = is_crouching
	crouching_collision_shape.disabled = !is_crouching
	
func handle_movement(delta: float, lerp_modifier: float, input_dir: Vector2):
	if (Input.is_action_pressed("sprint")):
		if (is_on_floor()):
			current_speed = crouch_sprint_speed if is_crouching else sprint_speed
			head_bobbing_current_intensity = head_bobbing_crouch_sprint_intensity if is_crouching else head_bobbing_sprint_intensity
			head_bobbing_index += head_bobbing_crouch_sprint_speed * delta if is_crouching else head_bobbing_sprint_speed * delta
		else: 
			# Don't let the player start sprinting while in the air.
			current_speed = prev_speed
	else:
		current_speed = crouch_walk_speed if is_crouching else walk_speed	
		head_bobbing_current_intensity = head_bobbing_crouch_walk_intensity if is_crouching else head_bobbing_walk_intensity
		head_bobbing_index += head_bobbing_crouch_walk_speed * delta if is_crouching else head_bobbing_walk_speed * delta
		
	if (is_on_floor() && input_dir != Vector2.ZERO):
		head_bobbing_vector.y = sin(head_bobbing_index)
		head_bobbing_vector.x = sin(head_bobbing_index / 2) + 0.5
		eyes.position.y = lerp(eyes.position.y, head_bobbing_vector.y * (head_bobbing_current_intensity / 2), lerp_modifier)
		eyes.position.x = lerp(eyes.position.x, head_bobbing_vector.x * head_bobbing_current_intensity, lerp_modifier)
	else:
		eyes.position.y = lerp(eyes.position.y, 0.0, lerp_modifier)
		eyes.position.x = lerp(eyes.position.x, 0.0, lerp_modifier)
	
func handle_airtime():
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
		
	var blocked = is_crouching and ray_cast_3d.is_colliding()
	if (Input.is_action_just_pressed("crouch") and !blocked and is_on_floor()):
		is_crouching = !is_crouching
		
	if (Input.is_action_just_pressed("ui_accept") and !ray_cast_3d.is_colliding() and is_on_floor()):
		is_crouching = false
		velocity.y = jump_velocity

		
func _physics_process(delta):
	var lerp_modifier = delta * lerp_speed
	var input_dir = Input.get_vector("left", "right", "forward", "backward")
	
	# Add the gravity.
	if not is_on_floor():
		velocity.y -= gravity * delta
	
	handle_movement(delta, lerp_modifier, input_dir)	
	handle_crouch(lerp_modifier)
	handle_airtime()
	handle_free_look(lerp_modifier)
	
	# Get the input direction and handle the movement/deceleration.
	# As good practice, you should replace UI actions with custom gameplay actions.
	
	direction = lerp(direction, (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized(), lerp_modifier)
	if direction:
		velocity.x = direction.x * current_speed
		velocity.z = direction.z * current_speed
	else:
		velocity.x = move_toward(velocity.x, 0, current_speed)
		velocity.z = move_toward(velocity.z, 0, current_speed)

	move_and_slide()
