#version 330

out vec4 finalColor;

// 硬件深度 gl_FragCoord.z（[0,1]）RGB 24 位打包；A 固定 1，避免默认透明混合污染深度。
void main()
{
    float depth = gl_FragCoord.z;
    vec3 enc = fract(vec3(1.0, 255.0, 65025.0) * depth);
    enc -= enc.yzz * vec3(1.0 / 255.0, 1.0 / 255.0, 0.0);
    finalColor = vec4(enc, 1.0);
}
