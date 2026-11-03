#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOriginalBc3NoiseDiagnosticTests
{
    [Test]
    public void OriginalBc3Noise_DiagnoseHardwareVersusPreparedValueExpansion()
    {
        if(Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST")!="1")Assert.Ignore("Native GPU is opt-in.");
        var official=new VfxOfficialDistortionPixelTests();official.LoadCurrentOfficialResourcesOnly();
        var bytes=(byte[])typeof(VfxOfficialDistortionPixelTests).GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(official,["commontextures/default_noise1.dds"])!;
        var sha=Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.That(sha,Is.EqualTo("957f2d12f5fc4be156ed4a1da1685ad5ffb905b2780e150c6841ebaadd87a3e8"));
        Assert.That(Encoding.ASCII.GetString(bytes,84,4),Is.EqualTo("DXT5"));
        var decoded=VfxPreparedResources.DecodeTexture(bytes,VfxTextureUsage.MaterialValue);
        Assert.That(decoded.Srgb,Is.False);Assert.That(decoded.MipLevels,Is.EqualTo(1));
        Assert.That(decoded.Width,Is.EqualTo(600));Assert.That(decoded.Height,Is.EqualTo(600));
        var graphics=new GraphicsDeviceServiceMock();
        try
        {
            var device=graphics.GraphicsDevice;
            using var compressed=new Texture2D(device,decoded.Width,decoded.Height,false,SurfaceFormat.Dxt5);
            compressed.SetData(bytes.AsSpan(128).ToArray());
            using var rgba=new Texture2D(device,decoded.Width,decoded.Height,false,SurfaceFormat.Color);rgba.SetData(decoded.Pixels);
            var native=Read(compressed);var prepared=Read(rgba);
            var maximum=new double[4];var changed=new int[4];
            for(var index=0;index<native.Length;index++)for(var channel=0;channel<4;channel++)
            {
                var difference=Math.Abs(Component(native[index],channel)-Component(prepared[index],channel));
                maximum[channel]=Math.Max(maximum[channel],difference);if(difference>.0000001)changed[channel]++;
            }
            var redNative=native.Select(value=>value.X).Order().ToArray();var redPrepared=prepared.Select(value=>value.X).Order().ToArray();
            var thresholds=Enumerable.Range(0,1001).Select(alpha=>1-alpha*.001f).ToArray();
            int[] Counts(float[] sorted)=>thresholds.Select(threshold=>
            {
                var low=0;var high=sorted.Length;
                while(low<high){var middle=(low+high)/2;if(sorted[middle]<threshold)low=middle+1;else high=middle;}
                return sorted.Length-low;
            }).ToArray();
            var evidence=new
            {
                Scope="Diagnostic collection only: exact unchanged original BC3 payload sampled by hardware versus the current prepared RGBA pixels. Point sampling to float32 removes filtering/mip/RT8bit ambiguity. This probe does not by itself assert decoder equivalence or game appearance.",
                Source="commontextures/default_noise1.dds",Sha256=sha,decoded.Width,decoded.Height,
                PreparedPixelSha256=Convert.ToHexStringLower(SHA256.HashData(decoded.Pixels)),
                MaximumNormalizedChannelDifference=maximum,PixelsDifferingPerChannel=changed,
                AlphaFloorThousandths=Enumerable.Range(0,1001).ToArray(),Thresholds=thresholds,
                HardwareRedFieldNonnegativeCounts=Counts(redNative),PreparedRedFieldNonnegativeCounts=Counts(redPrepared),
                Texels=native.Length,HardwareRedMinimum=redNative[0],HardwareRedMaximum=redNative[^1],
                PreparedRedMinimum=redPrepared[0],PreparedRedMaximum=redPrepared[^1],
            };
            var text=JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true});
            TestContext.Out.WriteLine(text);
            var output=Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
            if(!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"original-bc3-noise-diagnostic.json"),text);
            }
            Assert.That(native.All(value=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z)&&float.IsFinite(value.W)),Is.True);

            Vector4[] Read(Texture2D source)
            {
                using var target=new RenderTarget2D(device,decoded.Width,decoded.Height,false,SurfaceFormat.Vector4,DepthFormat.None);
                using var batch=new SpriteBatch(device);device.SetRenderTarget(target);device.Clear(Color.Transparent);
                batch.Begin(SpriteSortMode.Immediate,BlendState.Opaque,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
                batch.Draw(source,new Rectangle(0,0,decoded.Width,decoded.Height),Color.White);batch.End();device.SetRenderTarget(null);
                var result=new Vector4[decoded.Width*decoded.Height];target.GetData(result);return result;
            }
        }
        finally{graphics.Release();}
    }

    private static float Component(Vector4 value,int channel)=>channel switch{0=>value.X,1=>value.Y,2=>value.Z,_=>value.W};
}
