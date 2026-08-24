#version 330

layout(location = 0) in vec3 vertexPosition;
layout(location = 9) in mat4 instanceTransform;

uniform mat4 mvp;

void main()
{
    gl_Position = mvp * instanceTransform * vec4(vertexPosition, 1.0);
}
