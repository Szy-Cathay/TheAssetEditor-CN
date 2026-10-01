using System.IO;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Shared.ByteParsing;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.CampaignAnimBin
{
    public class CampaignAnimBinToXmlConverter : ITextConverter
    {
        public string GetText(byte[] bytes)
        {
            try
            {
                var bin = CampaignAnimationBinLoader.Load(new ByteChunk(bytes));
                var xmlserializer = new XmlSerializer(typeof(CampaignAnimationBin));
                var stringWriter = new StringWriter();
                using (var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings() { Indent = true }))
                {
                    xmlserializer.Serialize(writer, bin);
                    var str = stringWriter.ToString();
                    return str;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.UnableToOpenFile", e.Message));
                return "";
            }
        }
       
        public byte[] ToBytes(string text, string filePath, IPackFileService pfs, out ITextConverter.SaveError error)
        {
            try
            {
                var xmlserializer = new XmlSerializer(typeof(CampaignAnimationBin));
                using var stringReader = new StringReader(text);
                var reader = XmlReader.Create(stringReader);

                var errorHandler = new XmlSerializationErrorHandler();

                var obj = xmlserializer.Deserialize(reader, errorHandler.EventHandler);
                var typedObject = obj as CampaignAnimationBin;
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                if (errorHandler.Error != null)
                {
                    error = errorHandler.Error;
                    return null;
                }
                if (typedObject == null)
                    throw new InvalidDataException(LocalizationManager.Instance.Get("AnimPack.Campaign.InvalidXml"));
                var report = Validator.Check(typedObject, pfs, filePath);
                if (report.Errors.Any(item => item.IsError))
                {
                    error = new ITextConverter.SaveError { Text = string.Join(Environment.NewLine, report.Errors.Where(item => item.IsError).Select(item => item.Description)) };
                    return null;
                }
                var bytes = CampaignAnimationBinLoader.Write(typedObject, fileName);
                error = null;
                return bytes;

            }
            catch (Exception e)
            {
                var inner = ExceptionHelper.GetInnerMostException(e);
                if (inner is XmlException xmlException)
                    error = new ITextConverter.SaveError() { Text = xmlException.Message, ErrorLineNumber = xmlException.LineNumber, ErrorPosition = xmlException.LinePosition, ErrorLength = 0 };
                else
                    error = new ITextConverter.SaveError() { Text = e.Message };

                return null;
            }
        }

        public bool ShouldShowLineNumbers() => true;
        public string GetSyntaxType() => "XML";
        public bool CanSaveOnError() => false;
    }
}
