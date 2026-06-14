#version 330

in vec2 fragTexCoord;
out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;

void main()
{
    finalColor = texture(texture0, fragTexCoord) * colDiffuse * tint;
}
