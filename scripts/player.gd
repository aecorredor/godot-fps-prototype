extends CharacterBody3D

const crouch_depth = 0.5
const walking_speed = 5.0
const sprinting_speed = 8.5
const crouch_sprinting_speed = 4.0
const crouch_speed = 2.0
const jump_velocity = 4.5
const mouse_sensitivity = 0.25
const lerp_speed = 10.0
var current_speed = walking_speed
var prev_speed = current_speed
var direction = Vector3.ZERO
var is_crouching = false
var is_jumping = false
var is_free_looking = false

@onready var neck = $neck
@onready var head = $neck/head
@onready var standing_collision_shape = $standing_collision_shape
@onready var crouching_collision_shape = $crouching_collision_shape
@onready var ray_cast_3d = $RayCast3D

# Get the gravity from the project settings to be synced with RigidBody nodes.
var gravity = ProjectSettings.get_setting("physics/3d/default_gravity")

func handle_crouch(lerp_modifier: float): 
	var crouched_height = lerp(head.position.y, -crouch_depth, lerp_modifier)
	var standing_height = lerp(head.position.y, 0.0, lerp_modifier)
	head.position.y = crouched_height  if is_crouching else standing_height
	standing_collision_shape.disabled = is_crouching
	crouching_collision_shape.disabled = !is_crouching
	
func handle_sprint():
	if (Input.is_action_pressed("sprint")):
		if (is_on_floor()):
			current_speed = crouch_sprinting_speed if is_crouching else sprinting_speed
		else:
			current_speed = prev_speed
	else:
		current_speed = crouch_speed if is_crouching else walking_speed	
		
func handle_airtime():
	prev_speed = current_speed
	
	if (!is_on_floor()):
		current_speed = current_speed - 2.0
	else:
		current_speed = prev_speed
		
func handle_free_look(lerp_modifier: float):
	if (Input.is_action_pressed("free_look")):
		is_free_looking = true
	else:
		is_free_looking = false
		neck.rotation.y = lerp(neck.rotation.y, 0.0, lerp_modifier)
		head.rotation.x = lerp(head.rotation.x, 0.0, lerp_modifier)

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
		
	var crouchingAndBlocked = is_crouching and ray_cast_3d.is_colliding()
	if (Input.is_action_just_pressed("crouch") and !crouchingAndBlocked and is_on_floor()):
		is_crouching = !is_crouching
		
	if (Input.is_action_just_pressed("ui_accept") and !ray_cast_3d.is_colliding() and is_on_floor()):
		is_crouching = false
		velocity.y = jump_velocity

		
func _physics_process(delta):
	handle_sprint()
	var lerp_modifier = delta * lerp_speed
	handle_crouch(lerp_modifier)
	handle_airtime()
	handle_free_look(lerp_modifier)
	
	# Add the gravity.
	if not is_on_floor():
		velocity.y -= gravity * delta
	
	# Get the input direction and handle the movement/deceleration.
	# As good practice, you should replace UI actions with custom gameplay actions.
	var input_dir = Input.get_vector("left", "right", "forward", "backward")
	direction = lerp(direction, (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized(), lerp_modifier)
	if direction:
		velocity.x = direction.x * current_speed
		velocity.z = direction.z * current_speed
	else:
		velocity.x = move_toward(velocity.x, 0, current_speed)
		velocity.z = move_toward(velocity.z, 0, current_speed)

	move_and_slide()
