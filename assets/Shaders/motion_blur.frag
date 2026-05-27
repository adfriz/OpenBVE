#version 150 core

in vec2 textureCoord;
out vec4 fragColor;

uniform sampler2D uColorTexture;
uniform sampler2D uDepthTexture;

uniform mat4 uCurrentViewProjectionInverse;
uniform mat4 uPreviousViewProjection;
uniform vec3 uCameraOffset;

uniform int uNumSamples;
uniform float uStrength;

void main()
{
    float depth = texture(uDepthTexture, textureCoord).r;
    
    // Reconstruct NDC position
    vec4 ndc = vec4(textureCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    
    // Transform to camera-space / relative world space
    vec4 worldPos = uCurrentViewProjectionInverse * ndc;
    worldPos /= worldPos.w;
    
    // Transform to previous clip space position (including camera translation and rotation)
    vec3 prevRelativePos = worldPos.xyz + uCameraOffset;
    vec4 prevClipPos = uPreviousViewProjection * vec4(prevRelativePos, 1.0);
    prevClipPos /= prevClipPos.w;
    
    // Velocity vector in UV space
    vec2 velocity = (ndc.xy - prevClipPos.xy) * 0.5;
    
    // Scale velocity by strength
    velocity *= uStrength;
    
    // Avoid blurring sky/far background (depth close to 1.0)
    // 0.9999 corresponds to ~500m under near=0.1, far=1000.
    float farMask = 1.0 - smoothstep(0.9999, 1.0, depth);
    velocity *= farMask;
    
    // Mask out velocity for train carriage/cab pixels (flagged with alpha = 0.0 by default.frag)
    float trainMask = texture(uColorTexture, textureCoord).a < 0.05 ? 0.0 : 1.0;
    velocity *= trainMask;
    
    // Clamp to a reasonable maximum velocity to avoid visual separation artifacts
    float maxVelocity = 0.05;
    float len = length(velocity);
    if (len > maxVelocity)
    {
        velocity = (velocity / len) * maxVelocity;
    }
    
    // Accumulate samples along the velocity vector
    vec4 color = vec4(0.0);
    for (int i = 0; i < uNumSamples; ++i)
    {
        // Sample symmetrically around the pixel
        float t = float(i) / float(uNumSamples - 1) - 0.5;
        vec2 offset = velocity * t;
        color += texture(uColorTexture, textureCoord + offset);
    }
    
    fragColor = vec4(color.rgb / float(uNumSamples), 1.0);
}
