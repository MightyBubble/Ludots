#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;

out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
out vec4 clipSpace;
out vec2 dudvCoords;

const float tiling = 0.08;

void main()
{
    vec4 worldPos = matModel * vec4(vertexPosition, 1.0);
    fragPos = worldPos.xyz;
    fragNormal = normalize(mat3(matModel) * vertexNormal);
    fragColor = vertexColor;
    clipSpace = mvp * vec4(vertexPosition, 1.0);
    dudvCoords = worldPos.xz * tiling;
    gl_Position = clipSpace;
}
