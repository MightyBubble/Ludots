#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;

uniform mat4 mvp;
uniform mat4 matModel;
uniform float receiverDepthBias;

out vec3 fragPos;
out vec3 fragNormal;

void main()
{
    vec3 n = normalize(mat3(matModel) * vertexNormal);
    fragPos = (matModel * vec4(vertexPosition, 1.0)).xyz;
    fragNormal = n;
    vec4 clip = mvp * vec4(vertexPosition, 1.0);
    // Clip-depth only. Along-normal vertex push slides steep slopes off the
    // painted pixels and lets the grass pass show through the stamp.
    clip.z -= receiverDepthBias * clip.w * 1.0e-4;
    gl_Position = clip;
}
