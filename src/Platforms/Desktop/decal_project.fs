#version 330

in vec3 fragPos;
in vec3 fragNormal;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform mat4 matWorldToDecal;
uniform float alphaCutoff;
uniform float minReceiverNDotUp;

out vec4 finalColor;

void main()
{
    // Reject undersides / cliffs so the stamp reads as painted onto walkable ground.
    if (fragNormal.y < minReceiverNDotUp)
    {
        discard;
    }

    vec4 local = matWorldToDecal * vec4(fragPos, 1.0);
    vec3 a = abs(local.xyz);
    if (a.x > 0.5 || a.y > 0.5 || a.z > 0.5)
    {
        discard;
    }

    vec2 uv = local.xz + vec2(0.5);
    vec4 texel = texture(texture0, uv);
    vec4 color = texel * colDiffuse * tint;
    if (color.a < alphaCutoff)
    {
        discard;
    }

    finalColor = color;
}
