#version 330

in vec3 vertexPosition;

uniform mat4 mvp;
uniform mat4 matModel;

out vec3 fragDirection;

void main()
{
    vec4 worldPos = matModel * vec4(vertexPosition, 1.0);
    vec3 worldOrigin = vec3(matModel[3][0], matModel[3][1], matModel[3][2]);
    fragDirection = normalize(worldPos.xyz - worldOrigin);
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
