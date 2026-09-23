#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;

uniform mat4 mvp;
uniform mat4 matModel;

out vec2 fragTexCoord;
out vec3 fragNormal;
out vec3 fragPos;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(mat3(matModel) * vertexNormal);
    fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
