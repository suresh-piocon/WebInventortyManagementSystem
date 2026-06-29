using System;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace InventoryManagement.Client.Services
{
    public class BarcodeGeneratorService
    {
        public string GenerateQRCodeSvg(string content)
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new SvgQRCode(qrCodeData);
                // Return SVG raw content
                return qrCode.GetGraphic(5, "#000000", "#FFFFFF", false);
            }
            catch (Exception ex)
            {
                return $"<svg><text>QR Error: {ex.Message}</text></svg>";
            }
        }

        public string GenerateBarcodeSvg(string barcodeText)
        {
            try
            {
                var writer = new BarcodeWriterSvg
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Width = 250,
                        Height = 70,
                        Margin = 1,
                        PureBarcode = false // Include text underneath barcode
                    }
                };

                var svgImage = writer.Write(barcodeText);
                return svgImage.Content;
            }
            catch (Exception ex)
            {
                return $"<svg><text>Barcode Error: {ex.Message}</text></svg>";
            }
        }
    }
}
