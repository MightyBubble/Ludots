#version 330

out vec4 finalColor;

uniform vec4 colDiffuse;
uniform vec4 tint;

void main()
{
    finalColor = colDiffuse * tint;
}
