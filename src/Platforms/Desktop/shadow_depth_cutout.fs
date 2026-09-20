#version 330

in vec2 fragTexCoord;

uniform sampler2D texture0;
uniform float alphaCutoff;

out vec4 finalColor;

// 镂空投影：低于阈值的纹素 discard 不产生深度；通过者必须与 shadow_depth.fs
// 逐字相同的 RGB24 打包，接收端只认这一种编码（RaylibShaderContractTests 锁定）。
void main()
{
    float alpha = texture(texture0, fragTexCoord).a;
    if (alpha < alphaCutoff)
    {
        discard;
    }

    float depth = gl_FragCoord.z;
    vec3 enc = fract(vec3(1.0, 255.0, 65025.0) * depth);
    enc -= enc.yzz * vec3(1.0 / 255.0, 1.0 / 255.0, 0.0);
    finalColor = vec4(enc, 1.0);
}
