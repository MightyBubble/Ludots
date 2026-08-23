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
    // Nudge along the receiver normal so the re-drawn terrain triangles win the
    // depth test against the opaque pass without a separate depth-bias API.
    vec3 biasedPosition = vertexPosition + (vertexNormal * receiverDepthBias);
    fragPos = (matModel * vec4(vertexPosition, 1.0)).xyz;
    fragNormal = n;
    gl_Position = mvp * vec4(biasedPosition, 1.0);
}
