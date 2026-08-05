#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;

uniform mat4 mvp;
uniform mat4 matModel;
uniform float uTime;
uniform float uWaveAmplitude;
uniform float uWaveFrequency;
uniform float uWaveSpeed;

out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
out float fragWave;

void main()
{
    float phaseA = (vertexPosition.x * uWaveFrequency) + (uTime * uWaveSpeed);
    float phaseB = ((vertexPosition.z + vertexPosition.x * 0.37) * uWaveFrequency * 0.73) - (uTime * uWaveSpeed * 1.31);
    float wave = (sin(phaseA) + cos(phaseB)) * 0.5;
    vec3 displaced = vertexPosition + vec3(0.0, wave * uWaveAmplitude, 0.0);

    float dx = cos(phaseA) * uWaveFrequency * uWaveAmplitude * 0.5;
    float dz = -sin(phaseB) * uWaveFrequency * 0.73 * uWaveAmplitude * 0.5;
    vec3 waveNormal = normalize(vec3(-dx, 1.0, -dz));

    vec4 worldPos = matModel * vec4(displaced, 1.0);
    fragPos = worldPos.xyz;
    fragNormal = normalize(mat3(matModel) * waveNormal);
    fragColor = vertexColor;
    fragWave = wave;
    gl_Position = mvp * vec4(displaced, 1.0);
}
