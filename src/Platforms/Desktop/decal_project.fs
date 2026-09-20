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
    // Ground stamps: clip only in the stamp plane (XZ). Vertical extent is handled by
    // the CPU projector AABB / thickness so ridges are not sliced by a thin box lid.
    if (abs(local.x) > 0.5 || abs(local.z) > 0.5)
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
