#pragma once

inline std::string FungiParameterHeader(bool vertexStage = false)
{
    std::string code = R"(
cbuffer AecnFungiParameters : register(b2) { float4 fungiParameters[20]; };
#define Fu(i) fungiParameters[(i)/4][(i)%4]
#define Fu2(i) float2(Fu(i),Fu((i)+1))
#define Fu3(i) float3(Fu(i),Fu((i)+1),Fu((i)+2))
)";
    code += vertexStage ? R"(
#define AECN_FUNGI_VERTEX 1
Texture2D fungiVSDisplacement : register(t8);
SamplerState fungiVSSampler : register(s8);
float FungiDisplacement(float2 uv) { return fungiVSDisplacement.SampleLevel(fungiVSSampler,uv,0).b; }
)" : R"(
#define AECN_FUNGI_VERTEX 0
Texture2D fungiMaterialTexture : register(t8);
Texture2D fungiNormalTexture : register(t9);
Texture2D fungiDisplacementTexture : register(t10);
SamplerState fungiExtraSampler : register(s8);
float FungiDisplacement(float2 uv) { return fungiDisplacementTexture.SampleLevel(fungiExtraSampler,uv,0).b; }
)";
    code += R"(
float FungiOffset(float3 p,float3 n,float2 uv,float red,float time)
{
    // Both native stages broadcast X oscillation and normal-X displacement to XYZ.
    float phase=(dot(p,float3(1,1,1))*Fu(22)+time*Fu(14))*Fu(24);
    float oscillation=sin(phase)*Fu(23)*red;
    float2 displacementUV=(uv+time*Fu2(16))*Fu2(18);
    float displacement=FungiDisplacement(displacementUV)*n.x*Fu(15)*(1-uv.y)*Fu(20);
    return Fu(21)*oscillation+displacement;
}
)";
    if (vertexStage) return code;
    // Closed native UI lighting branch: six-face ambient and direct sun, no GI/SH/cloud/shadow/IBL resources.
    code += R"(
float FungiAcos(float x)
{
    x=clamp(x,-1,1); float a=abs(x);
    float value=sqrt(1-a)*(((-0.018729*a+0.074261)*a-0.212114)*a+1.570729);
    return x<0 ? 3.141592653589793-value : value;
}
float FungiCdf(float x)
{
    float a=abs(x),square=a*a;
    float polynomial=1+0.278393*a+0.230389*square+0.000972*square*a+0.078108*square*square;
    float inverse=1/(polynomial*polynomial*polynomial*polynomial);
    return .5*(1+sign(x)*(1-inverse));
}
float3 FungiFresnel(float3 f0,float cosine)
{
    float c=saturate(cosine);
    return f0+(1-f0)*exp2((-5.55473*c-6.98316)*c);
}
float3 FungiLighting(float3 base,float metal,float rough,float3 n,float3 v,
    float3 px,float3 nx,float3 py,float3 ny,float3 pz,float3 nz,float3 sunDirection,float3 sunColour)
{
    float3 f0=lerp(float3(.04,.04,.04),base,metal);
    float3 diffuse=base*(1-metal);
    float3 cube=n.x*n.x*(n.x<0?nx:px)+n.y*n.y*(n.y<0?ny:py)+n.z*n.z*(n.z<0?nz:pz);
    float3 ambient=cube*diffuse*(1-f0);
    if (dot(sunDirection,sunDirection)==0 || all(sunColour==float3(0,0,0))) return ambient;
    float3 l=normalize(-sunDirection);
    float nl=dot(n,l),nv=dot(n,v),nlSat=saturate(nl),nvSat=saturate(nv);
    float smooth=1-rough;
    float sigma=1.1284-1.125249*smooth;
    float angle=FungiAcos(dot(l,reflect(-v,n)));
    float radius=.008726646259971648;
    float population=abs(FungiCdf((angle+radius)/sigma*.7071067811865475)-FungiCdf((angle-radius)/sigma*.7071067811865475));
    float allFacets=FungiCdf(1.570796326794897/sigma*.7071067811865475)-FungiCdf(-1.570796326794897/sigma*.7071067811865475);
    float facets=saturate(population/allFacets); facets=facets*facets*.7853981633974483;
    float k=rough*rough*.5;
    float visibility=saturate(.25/max((nlSat*(1-k)+k)*(nvSat*(1-k)+k),1e-20));
    visibility*=lerp(1,nlSat,sin(rough*1.570796326794897));
    float3 h=normalize(v+l);
    float3 specular=FungiFresnel(f0,dot(v,h))*facets*visibility;
    float3 scattering=1-FungiFresnel(f0,nlSat);
    // The native UI branch reuses the converted microfacet width for Oren-Nayar.
    float square=sigma*sigma;
    float A=1-square/(2*(square+.33)),B=.45*square/(square+.09);
    float thetaL=FungiAcos(nl),thetaV=FungiAcos(nv);
    float oren=A+B*max(cos(thetaL-thetaV),0)*sin(max(thetaL,thetaV))*tan(min(thetaL,thetaV));
    float3 direct=specular*sunColour+diffuse*scattering*sunColour*oren*max(nl,0)*.00007615242180438042;
    return ambient+direct;
}
)";
    return code;
}

inline std::string FungiVertexOffset()
{
    return R"(
    float fungiTime=lerp(customData1.z,customData1.x,Fu(13));
    worldPos+=FungiOffset(worldPos,worldNormal,uv1,vcolor.r,fungiTime);
)";
}

inline std::string FungiMeshCode()
{
    return R"(
#if AECN_FUNGI_VERTEX
        $F3$ emissive=$F3$(0,0,0);
        $F1$ opacity=1;
        $F1$ opacityMask=1;
        $F3$ worldPositionOffset=$F3$(0,0,0);
#else
        $F4$ baseSample=$TEX_P0$uv1$TEX_S0$;
        $F4$ mask=$TEX_P5$uv1$TEX_S5$;
        $F2$ ditherUV=screenUV*fungiRender.xy*.125;
        $F1$ dither=$TEX_P1$ditherUV$TEX_S1$.r;
        $F1$ alphaByte=floor(saturate(customData2.a)*255+.5);
        $F1$ packedAlpha=floor(alphaByte*(1.0/255.0)*1000)*.001;
        $F1$ opacityMask=baseSample.a>=.5 && packedAlpha>=dither ? 1 : 0;
        $F1$ opacity=1;
        $F1$ time=customData1.x;
        $F1$ animationTime=lerp(customData1.z,time,Fu(13));
        $F3$ facingPosition=worldPos+FungiOffset(worldPos,worldNormal,uv1,vcolor.r,animationTime);
        $F1$ ndv=abs(dot(normalize(cameraPosition.xyz-facingPosition),normalize(worldNormal)));
        $F1$ facing=lerp(pow(abs(1-ndv),Fu(11)),pow(ndv,Fu(11)),Fu(10));
        $F4$ directionSample=$TEX_P2$uv1$TEX_S2$;
        $F2$ direction=pow(abs(directionSample.rb),.45)*2-1;
        $F4$ distortion=$TEX_P3$uv1$TEX_S3$;
        $F2$ emissionUV=(uv1+distortion.rb*Fu(0)+direction*time*Fu(3)*.05)*Fu2(5);
        $F1$ emission=$TEX_P4$emissionUV$TEX_S4$.r*Fu(4);
        emission+=(sin((customData1.y+time+.5)*Fu(1))+.99)*Fu(2)*.1;
        $F3$ glow=(facing+emission)*Fu3(7)*mask.a;
        $F1$ peak=max(max(glow.r,glow.g),glow.b);
        $F1$ intensityScale=lerp(1,peak,saturate(floor(peak)));
        $F3$ normalizedGlow=glow/intensityScale;
        $F1$ luminance=saturate(dot(glow,$F3$(.2126,.7152,.0722)));
        $F3$ baseColour=baseSample.rgb*lerp($F3$(1,1,1),fungiFaction0.rgb,mask.r);
        baseColour*=lerp($F3$(1,1,1),fungiFaction1.rgb,mask.g);
        baseColour=saturate(1-(1-baseColour)*(1-normalizedGlow));
        $F4$ materialMap=fungiMaterialTexture.Sample(fungiExtraSampler,uv1);
        $F1$ metal=saturate(materialMap.r*(1-mask.r*Fu(12)*(customData1.y-.5))*(1-luminance));
        $F1$ rough=saturate(lerp(materialMap.g,1,luminance));
        $F4$ normalSample=fungiNormalTexture.Sample(fungiExtraSampler,uv1);
        $F2$ tangentNormal=(normalSample.ag+1.0/510.0)*2-1;
        $F3$ n=normalize(normalize(worldTangent)*tangentNormal.x+normalize(worldBinormal)*tangentNormal.y
            +normalize(worldNormal)*sqrt(max(0,1-dot(tangentNormal,tangentNormal))));
        $F3$ emissive=FungiLighting(baseColour,metal,rough,n,normalize(cameraPosition.xyz-worldPos),
            fungiAmbient0.rgb,fungiAmbient1.rgb,fungiAmbient2.rgb,fungiAmbient3.rgb,fungiAmbient4.rgb,fungiAmbient5.rgb,
            fungiSunDirection.xyz,fungiSunColour.rgb*(20000*fungiSunDirection.w));
        $F3$ worldPositionOffset=$F3$(0,0,0);
#endif
    )";
}

inline std::vector<uint8_t> FungiMeshMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer=[&](int32_t value){auto p=reinterpret_cast<const uint8_t*>(&value);bytes.insert(bytes.end(),p,p+4);};
    auto string=[&](const char* value){auto length=std::strlen(value)+1;integer(static_cast<int32_t>(length));bytes.insert(bytes.end(),value,value+length);};
    bytes.insert(bytes.end(),{'E','F','K','M'});integer(3);integer(0);integer(0);
    bytes.insert(bytes.end(),{'P','R','M','_'});auto chunk=bytes.size();integer(0);
    // Final native radiance is calculated explicitly, rather than Ef's generic light model.
    integer(1);integer(0);integer(0);integer(4);integer(4);integer(6);
    for(int index=0;index<6;++index){auto name="fungiTexture"+std::to_string(index);string(name.c_str());string(name.c_str());string("");integer(index);integer(0);integer(0);integer(1);integer(0);}
    integer(11);
    for(auto name:{"fungiRender","fungiAmbient0","fungiAmbient1","fungiAmbient2","fungiAmbient3","fungiAmbient4","fungiAmbient5","fungiSunDirection","fungiSunColour","fungiFaction0","fungiFaction1"})
    {string(name);string(name);integer(0);integer(0);integer(4);integer(0);integer(0);integer(0);integer(0);}
    auto size=static_cast<int32_t>(bytes.size()-chunk-4);std::memcpy(bytes.data()+chunk,&size,4);
    auto code=FungiMeshCode();bytes.insert(bytes.end(),{'G','E','N','E'});integer(static_cast<int32_t>(code.size()+5));string(code.c_str());
    return bytes;
}
