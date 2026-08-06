float3 ShadeViewportSolid(
    float3 surfaceNormal,
    float3 worldPosition,
    float3 cameraPosition)
{
    float3 normal = normalize(surfaceNormal);
    float3 viewDirection = normalize(
        cameraPosition - worldPosition);
    float3 referenceUp = abs(viewDirection.y) > 0.98f
        ? float3(0.0f, 0.0f, 1.0f)
        : float3(0.0f, 1.0f, 0.0f);
    float3 viewRight = normalize(cross(
        referenceUp,
        viewDirection));
    float3 viewUp = normalize(cross(
        viewDirection,
        viewRight));
    float3 keyDirection = normalize(
        viewDirection -
        viewRight * 0.35f +
        viewUp * 0.55f);
    float3 fillDirection = normalize(
        viewDirection +
        viewRight * 0.55f -
        viewUp * 0.15f);

    float key = saturate(dot(normal, keyDirection));
    float fill = saturate(dot(normal, fillDirection));
    float rim = pow(
        1.0f - saturate(dot(normal, viewDirection)),
        3.0f);
    float light = 0.28f + key * 0.58f +
        fill * 0.18f + rim * 0.08f;
    float3 neutralMaterial = float3(
        0.56f,
        0.57f,
        0.59f);
    return pow(
        saturate(neutralMaterial * light),
        1.0f / 2.2f);
}
