#version 330

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec3 vertexNormal;
layout(location = 9) in mat4 instanceTransform;

uniform mat4 mvp;

out vec2 fragTexCoord;
out vec3 fragNormal;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(mat3(instanceTransform) * vertexNormal);
    gl_Position = mvp * instanceTransform * vec4(vertexPosition, 1.0);
}
