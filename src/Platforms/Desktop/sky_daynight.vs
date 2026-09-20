#version 330

in vec3 vertexPosition;

uniform mat4 matView;
uniform mat4 matProjection;

out vec3 fragDirection;

void main()
{
    fragDirection = normalize(vertexPosition);
    vec4 clip = matProjection * matView * vec4(vertexPosition, 1.0);
    gl_Position = clip.xyww;
}
