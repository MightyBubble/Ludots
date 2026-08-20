uniform sampler2D uShadowMap;
uniform mat4 uLightSpaceMatrix;
uniform float uShadowEnabled;
uniform float uShadowTexelWorld;
uniform float uShadowBias;

float UnpackDepth(vec4 packed)
{
    return dot(packed.rgb, vec3(1.0, 1.0 / 255.0, 1.0 / 65025.0));
}

float SampleShadow(vec3 worldPos, vec3 N)
{
    if (uShadowEnabled < 0.5)
    {
        return 1.0;
    }

    vec3 offsetPos = worldPos + N * uShadowTexelWorld;
    vec4 lightSpace = uLightSpaceMatrix * vec4(offsetPos, 1.0);
    vec3 proj = lightSpace.xyz / max(lightSpace.w, 1e-6);
    proj = proj * 0.5 + 0.5;
    if (proj.x < 0.0 || proj.x > 1.0 || proj.y < 0.0 || proj.y > 1.0 || proj.z > 1.0)
    {
        return 1.0;
    }

    float receiverDepth = proj.z;
    float texel = 1.0 / 2048.0;
    vec2 shadowUv = proj.xy;
    float lit = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float stored = UnpackDepth(texture(uShadowMap, shadowUv + vec2(x, y) * texel));
            lit += receiverDepth <= stored + uShadowBias ? 1.0 : 0.0;
        }
    }

    return lit / 9.0;
}
