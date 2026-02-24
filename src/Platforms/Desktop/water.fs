#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;

uniform vec3 uLightPos;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uLightIntensity;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightPos - fragPos);
    vec3 V = normalize(uViewPos - fragPos);
    vec3 H = normalize(L + V);

    float ndl = abs(dot(N, L));
    float spec = pow(max(dot(N, H), 0.0), 48.0) * 0.08;

    vec3 base = fragColor.rgb;
    vec3 lit = base * (uAmbient + uLightIntensity * ndl) + vec3(spec);
    finalColor = vec4(clamp(lit, 0.0, 1.0), fragColor.a);
}

