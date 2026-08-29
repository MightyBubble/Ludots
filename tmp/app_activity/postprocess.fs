#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec2 uResolution;
uniform float uTime;
uniform float uExposure;
uniform float uContrast;
uniform float uSaturation;
uniform float uVignetteStrength;

out vec4 finalColor;

void main()
{
    vec4 source = texture(texture0, fragTexCoord) * fragColor;
    vec3 color = source.rgb * uExposure;
    color = ((color - 0.5) * uContrast) + 0.5;

    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, uSaturation);

    vec2 uv = (fragTexCoord * 2.0) - 1.0;
    uv.x *= uResolution.x / max(uResolution.y, 1.0);
    float vignette = 1.0 - smoothstep(0.18, 1.85, dot(uv, uv));
    color *= mix(1.0, vignette, uVignetteStrength);

    float grain = fract(sin(dot(fragTexCoord * uResolution + uTime, vec2(12.9898, 78.233))) * 43758.5453);
    color += (grain - 0.5) * 0.006;

    finalColor = vec4(clamp(color, 0.0, 1.0), source.a);
}
