#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;

out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
out float fragHeightBand;

void main()
{
    vec4 worldPos = matModel * vec4(vertexPosition, 1.0);
    fragPos = worldPos.xyz;
    fragNormal = normalize(mat3(matModel) * vertexNormal);
    // RGB = biome tint; A packs heightBand 0-1 for albedo layer weights.
    fragColor = vec4(vertexColor.rgb, 1.0);
    fragHeightBand = vertexColor.a;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
