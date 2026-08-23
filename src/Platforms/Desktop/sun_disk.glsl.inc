// 太阳盘/光晕共享块：skybox.fs 与 sky_daynight.fs 唯一来源，参数由 RaylibSkyboxConfig 注入
uniform vec3 uSunDirection;
uniform vec3 uSunColor;
uniform float uSunDiskSharpness;
uniform float uSunDiskIntensity;
uniform float uSunGlowSharpness;
uniform float uSunGlowIntensity;

vec3 SunHalo(vec3 direction, vec3 sunColor)
{
    vec3 sunDirection = normalize(uSunDirection);
    float sunDot = max(dot(direction, sunDirection), 0.0);
    float sunDisk = pow(sunDot, uSunDiskSharpness);
    float sunGlow = pow(sunDot, uSunGlowSharpness) * uSunGlowIntensity;
    return sunColor * (sunGlow + sunDisk * uSunDiskIntensity);
}
