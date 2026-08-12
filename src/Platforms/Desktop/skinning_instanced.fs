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
    // Missing vertex-color attributes often arrive as zeros; do not multiply them in.
    vec4 color = fragColor;
    if (color.a <= 0.001)
    {
        color = vec4(1.0);
    }
    finalColor = texel * colDiffuse * tint * color;
}
