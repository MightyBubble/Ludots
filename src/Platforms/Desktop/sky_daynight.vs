#version 330

in vec3 vertexPosition;

uniform mat4 matView;
uniform mat4 matProjection;

out vec3 fragDirection;

void main()
{
    fragDirection = vertexPosition;
    gl_Position = matProjection * matView * vec4(vertexPosition, 1.0);
}
