#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;

out vec4 finalColor;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);
    vec4 color = texel * colDiffuse * tint * fragColor;
    if (color.a < 0.02)
    {
        discard;
    }

    finalColor = color;
}
