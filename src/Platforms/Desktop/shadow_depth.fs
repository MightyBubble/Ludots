#version 330

out vec4 finalColor;

// 硬件深度 gl_FragCoord.z（[0,1]）RGBA 256 进位打包；model_lit.fs UnpackDepth 对应解码
void main()
{
    float depth = gl_FragCoord.z;
    vec4 enc = vec4(1.0, 255.0, 65025.0, 16581375.0) * depth;
    enc = fract(enc);
    enc -= enc.yzww * vec4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);
    finalColor = enc;
}
