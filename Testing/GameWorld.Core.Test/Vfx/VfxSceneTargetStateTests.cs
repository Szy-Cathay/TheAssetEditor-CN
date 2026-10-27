using System.Runtime.InteropServices;
using GameWorld.Core.Vfx;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxSceneTargetStateTests
{
    [SetUp]
    public void RequireExplicitGpuOptIn()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1")
            Assert.Ignore("Scene target state restoration requires explicit offscreen GPU opt-in.");
    }

    [TestCase(1, true)]
    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(2, false)]
    [TestCase(7, true)]
    [TestCase(7, false)]
    [TestCase(8, true)]
    [TestCase(8, false)]
    public void SceneScope_RestoresAllOutputsAndPreservesAppendCounter(int targetCount, bool finish)
    {
        using var device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        var context = device.ImmediateContext;
        var textures = Enumerable.Range(0, targetCount).Select(_ => new Texture2D(device, new Texture2DDescription
        {
            Width = 32, Height = 32, MipLevels = 1, ArraySize = 1, Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        })).ToArray();
        var targets = textures.Select(texture => new RenderTargetView(device, texture)).ToArray();
        using var depthTexture = new Texture2D(device, new Texture2DDescription
        {
            Width = 32, Height = 32, MipLevels = 1, ArraySize = 1, Format = Format.D24_UNorm_S8_UInt,
            SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil,
        });
        using var depth = new DepthStencilView(device, depthTexture);
        using var buffer = new SharpDX.Direct3D11.Buffer(device, new BufferDescription
        {
            SizeInBytes = 128, StructureByteStride = 4, BindFlags = BindFlags.UnorderedAccess,
            OptionFlags = ResourceOptionFlags.BufferStructured,
        });
        using var unordered = new UnorderedAccessView(device, buffer, new UnorderedAccessViewDescription
        {
            Format = Format.Unknown, Dimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new UnorderedAccessViewDescription.BufferResource
            {
                FirstElement = 0, ElementCount = 32, Flags = UnorderedAccessViewBufferFlags.Append,
            },
        });
        using var preview = EffekseerNative.AecnVfxCreate(device.NativePointer, context.NativePointer);
        try
        {
            Assert.That(preview.IsInvalid, Is.False);
            if (targetCount < 8)
                context.OutputMerger.SetTargets(depth, targetCount, [unordered], [7], targets);
            else
                context.OutputMerger.SetTargets(depth, targets);
            var expected = ReadOutputs(context, targetCount < 8 ? targetCount : -1);
            if (targetCount < 8)
            {
                Assert.That(expected.Uav, Is.EqualTo(unordered.NativePointer), "The control must actually bind an OM UAV.");
                Assert.That(ReadCounter(device, unordered), Is.EqualTo(7));
            }
            var lighting = default(EffekseerLighting);
            EffekseerNative.Check(EffekseerNative.AecnVfxBeginScene(preview, in lighting));
            var active = ReadOutputs(context, targetCount < 8 ? targetCount : -1);
            Assert.That(active.Targets[0], Is.Not.EqualTo(expected.Targets[0]), "BeginScene must activate the floating intermediate.");
            Assert.That(active.Uav, Is.EqualTo(IntPtr.Zero), "The floating scene must detach caller UAVs while sampling particle resources.");
            if (finish) EffekseerNative.Check(EffekseerNative.AecnVfxEndScene(preview));
            else preview.Dispose();
            var actual = ReadOutputs(context, targetCount < 8 ? targetCount : -1);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Targets, Is.EqualTo(expected.Targets), "Restore the actual MRT slots without occupying the UAV slots as null RTVs.");
                Assert.That(actual.Depth, Is.EqualTo(expected.Depth));
                Assert.That(actual.Uav, Is.EqualTo(expected.Uav), "The shared scene must not discard the caller's OM UAV.");
                if (targetCount < 8)
                    Assert.That(ReadCounter(device, unordered), Is.EqualTo(7), "Restoring an append UAV must preserve its hidden counter.");
            });
        }
        finally
        {
            preview.Dispose();
            context.ClearState();
            foreach (var target in targets) target.Dispose();
            foreach (var texture in textures) texture.Dispose();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetOutputs(nint context, uint targetCount, nint targets, nint depth, uint uavStart, uint uavCount, out nint unordered);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetTargets(nint context, uint targetCount, nint targets, out nint depth);

    private static (nint[] Targets, nint Depth, nint Uav) ReadOutputs(DeviceContext context, int unorderedSlot)
    {
        var targets = new nint[8];
        var storage = Marshal.AllocHGlobal(targets.Length * IntPtr.Size);
        nint unordered = 0, depth = 0;
        try
        {
            // D3D11 context slots 89/90 query outputs, including valid null bindings that SharpDX rejects.
            var table = Marshal.ReadIntPtr(context.NativePointer);
            var queryTargets = Marshal.GetDelegateForFunctionPointer<GetTargets>(Marshal.ReadIntPtr(table, 89 * IntPtr.Size));
            queryTargets(context.NativePointer, (uint)targets.Length, storage, out depth);
            Marshal.Copy(storage, targets, 0, targets.Length);
            if (unorderedSlot >= 0)
            {
                var query = Marshal.GetDelegateForFunctionPointer<GetOutputs>(Marshal.ReadIntPtr(table, 90 * IntPtr.Size));
                query(context.NativePointer, 0, 0, 0, (uint)unorderedSlot, 1, out unordered);
            }
            return (targets, depth, unordered);
        }
        finally
        {
            foreach (var target in targets) if (target != 0) Marshal.Release(target);
            if (depth != 0) Marshal.Release(depth);
            if (unordered != 0) Marshal.Release(unordered);
            Marshal.FreeHGlobal(storage);
        }
    }

    private static int ReadCounter(Device device, UnorderedAccessView unordered)
    {
        using var counter = new SharpDX.Direct3D11.Buffer(device, new BufferDescription { SizeInBytes = 4 });
        using var staging = new SharpDX.Direct3D11.Buffer(device, new BufferDescription
        {
            SizeInBytes = 4, Usage = ResourceUsage.Staging, CpuAccessFlags = CpuAccessFlags.Read,
        });
        var context = device.ImmediateContext;
        context.CopyStructureCount(counter, 0, unordered);
        context.CopyResource(counter, staging);
        var mapped = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        try { return Marshal.ReadInt32(mapped.DataPointer); }
        finally { context.UnmapSubresource(staging, 0); }
    }
}
