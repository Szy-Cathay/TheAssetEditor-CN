#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D11;
using Buffer=SharpDX.Direct3D11.Buffer;
using Texture2D=SharpDX.Direct3D11.Texture2D;
using SamplerState=SharpDX.Direct3D11.SamplerState;
using RasterizerState=SharpDX.Direct3D11.RasterizerState;
using FillMode=SharpDX.Direct3D11.FillMode;
using CullMode=SharpDX.Direct3D11.CullMode;
using TextureAddressMode=SharpDX.Direct3D11.TextureAddressMode;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxFogBloodMaterialPixelsTests
{
    // An independent texture-filter primitive; no product material, vertex shader, MBOIT code or output is reused.
    private sealed class TextureSampler:IDisposable
    {
        private readonly GraphicsDevice _graphics;
        private readonly DeviceContext _context;
        private readonly VertexShader _vertex;
        private readonly PixelShader _pixel;
        private readonly Buffer _uv;
        private readonly SamplerState _sampler;
        private readonly RasterizerState _raster;
        private readonly Dictionary<Image,(Texture2D Texture,ShaderResourceView View)> _images=[];
        private readonly RenderTarget2D _target;
        internal TextureSampler(GraphicsDevice graphics,Image[] images)
        {
            _graphics=graphics;var direct=(SharpDX.Direct3D11.Device)graphics.Handle;_context=direct.ImmediateContext;
            const string vertex="float4 main(uint id:SV_VertexID):SV_POSITION{return float4(id==2?3:-1,id==1?3:-1,0,1);}";
            const string pixel="cbuffer Values:register(b0){float4 uv;};Texture2D image:register(t0);SamplerState linearWrap:register(s0);float4 main():SV_Target{return image.SampleLevel(linearWrap,uv.xy,0);}";
            _vertex=new(direct,Compile(vertex,"vs_5_0"));_pixel=new(direct,Compile(pixel,"ps_5_0"));
            _uv=new(direct,16,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            _sampler=new(direct,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Wrap,
                AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Wrap,MaximumLod=float.MaxValue,MaximumAnisotropy=1});
            _raster=new(direct,new RasterizerStateDescription{FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=true});
            _target=new(graphics,1,1,false,SurfaceFormat.Vector4,DepthFormat.None);
            foreach(var image in images)
            {
                using var data=new SharpDX.DataStream(image.Width*image.Height*4,true,true);data.Write(image.Pixels,0,image.Width*image.Height*4);data.Position=0;
                var texture=new Texture2D(direct,new Texture2DDescription{Width=image.Width,Height=image.Height,MipLevels=1,ArraySize=1,
                    Format=image.Srgb?SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb:SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                    SampleDescription=new(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource},new SharpDX.DataRectangle(data.DataPointer,image.Width*4));
                _images.Add(image,(texture,new ShaderResourceView(direct,texture)));
            }
        }
        internal Vector4 Sample(Image image,Vector2 uv)
        {
            _graphics.SetRenderTarget(_target);_context.Rasterizer.SetViewport(0,0,1,1);
            _context.Rasterizer.State=_raster;_context.OutputMerger.SetBlendState(null);_context.OutputMerger.SetDepthStencilState(null);
            _context.InputAssembler.InputLayout=null;_context.InputAssembler.PrimitiveTopology=SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            var value=new Vector4(uv,0,0);_context.UpdateSubresource(ref value,_uv);
            _context.VertexShader.Set(_vertex);_context.PixelShader.Set(_pixel);_context.PixelShader.SetConstantBuffer(0,_uv);
            _context.PixelShader.SetSampler(0,_sampler);_context.PixelShader.SetShaderResource(0,_images[image].View);_context.Draw(3,0);
            _context.PixelShader.SetShaderResource(0,null);_graphics.SetRenderTarget(null);
            var result=new Vector4[1];_target.GetData(result);
            TestContext.Out.WriteLine($"Independent hardware fetch: UV={uv}, size={image.Width}x{image.Height}, sRGB={image.Srgb}, actual={result[0]}, software={VfxFogBloodMaterialPixelsTests.Sample(image,uv)}");
            return result[0];
        }
        public void Dispose()
        {
            foreach(var pair in _images.Values){pair.View.Dispose();pair.Texture.Dispose();}
            _target.Dispose();_raster.Dispose();_sampler.Dispose();_uv.Dispose();_pixel.Dispose();_vertex.Dispose();
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate IntPtr BlobPointer(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate nuint BlobSize(IntPtr self);
        [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Ansi)]
        private static extern int D3DCompile(byte[] source,nuint length,string? name,IntPtr defines,IntPtr include,
            string entry,string profile,uint flags,uint effects,out IntPtr code,out IntPtr errors);
        private static byte[] Bytes(IntPtr blob)
        {
            var table=Marshal.ReadIntPtr(blob);var pointer=Marshal.GetDelegateForFunctionPointer<BlobPointer>(Marshal.ReadIntPtr(table,3*IntPtr.Size))(blob);
            var size=Marshal.GetDelegateForFunctionPointer<BlobSize>(Marshal.ReadIntPtr(table,4*IntPtr.Size))(blob);
            var result=new byte[checked((int)size)];Marshal.Copy(pointer,result,0,result.Length);return result;
        }
        private static byte[] Compile(string source,string profile)
        {
            var bytes=Encoding.ASCII.GetBytes(source);var code=IntPtr.Zero;var errors=IntPtr.Zero;
            try
            {
                var result=D3DCompile(bytes,(nuint)bytes.Length,null,IntPtr.Zero,IntPtr.Zero,"main",profile,0,0,out code,out errors);
                if(result<0)throw new InvalidOperationException(errors==IntPtr.Zero?$"D3DCompile {result:X8}":Encoding.ASCII.GetString(Bytes(errors)));
                return Bytes(code);
            }
            finally{if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
        }
    }
}
