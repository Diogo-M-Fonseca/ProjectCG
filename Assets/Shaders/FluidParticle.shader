Shader "Custom/FluidParticle"
{
    Properties {
        _Color ("Base Color", Color) = (0.1, 0.3, 1.0, 1.0)
        _Velocity ("Velocity", Vector) = (0, 0, 0, 0)
        _FlowIntensity ("Flow Intensity", Float) = 1.0
    }
    
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            struct appdata {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                uint instanceID : SV_InstanceID;
            };
            
            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };
            
            // UNIQUE TO YOU: Particle instance data
            StructuredBuffer<float4> _ParticlePositions;
            StructuredBuffer<float4> _ParticleVelocities;
            
            v2f vert (appdata v, uint instanceID : SV_InstanceID) {
                v2f o;
                
                // Get particle data from buffers (your friend doesn't have this)
                float4 particlePos = _ParticlePositions[instanceID];
                float4 particleVel = _ParticleVelocities[instanceID];
                
                // Apply velocity-based deformation (UNIQUE VISUAL EFFECT)
                float velocityStrength = length(particleVel.xyz);
                float3 deform = v.normal * velocityStrength * 0.1;
                
                // Position particle in world space
                float4 worldPos = float4(particlePos.xyz + deform, 1.0);
                o.pos = UnityObjectToClipPos(worldPos);
                o.worldPos = worldPos.xyz;
                o.normal = v.normal;
                
                // Color based on velocity (your visual style)
                o.color = float4(
                    0.1 + particleVel.x * 0.5,  // Red = X velocity
                    0.3 + particleVel.y * 0.5,  // Green = Y velocity  
                    1.0,                         // Blue base
                    0.8                          // Alpha
                );
                
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target {
                // Simple lighting + velocity glow
                float3 lightDir = normalize(float3(0.3, 1, 0.2));
                float diff = max(0, dot(i.normal, lightDir));
                
                // Add rim light effect (makes particles pop)
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = 1.0 - saturate(dot(viewDir, i.normal));
                rim = smoothstep(0.5, 1.0, rim);
                
                return i.color * (0.6 + 0.4 * diff + rim * 0.3);
            }
            ENDCG
        }
    }
}