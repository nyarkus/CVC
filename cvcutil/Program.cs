using System.CommandLine;

namespace cvcutil;

class Program
{
    static int Main(string[] args)
    {
        var rootCommand = new RootCommand("CVC util");
        
        var playCommand = new Command("play", "Plays a .ccv file");
        var playPathArgument = new Argument<string>("path");
        playCommand.Arguments.Add(playPathArgument);
        
        var charsetOption = new Option<string>("--charset");
        charsetOption.Description = "Which symbols to use in the render. In order of increasing brightness";
        
        var charsetPresetOption = new Option<string?>("--charset-preset");
        charsetPresetOption.Aliases.Add("-p");
        charsetPresetOption.Description = "Character set preset. Presets: classic, bricks, binary ";
        charsetPresetOption.DefaultValueFactory = _ => "classic";
        
        playCommand.Options.Add(charsetOption);
        playCommand.Options.Add(charsetPresetOption);
        playCommand.SetAction(parseResult =>
            PlayHandler.Play(
                parseResult.GetValue(playPathArgument)!,
                parseResult.GetValue(charsetOption),
                parseResult.GetValue(charsetPresetOption)));

        var convertCommand = new Command("convert", "Converts a video file to .ccv");
        var inputArgument = new Argument<string>("input");
        var outputOption = new Option<string?>("--output");
        outputOption.Aliases.Add("-o");
        outputOption.Description = "Where to save the .ccv file. Defaults to input path with .ccv extension.";

        var widthOption = new Option<int>("--width");
        widthOption.Aliases.Add("-w");
        widthOption.Description = "Output width in console characters.";
        widthOption.Required = true;

        var heightOption = new Option<int>("--height");
        heightOption.Aliases.Add("-h");
        heightOption.Description = "Output height in console characters.";
        heightOption.Required = true;

        var colorsOption = new Option<byte>("--colors");
        colorsOption.Aliases.Add("-c");
        colorsOption.Description = "Number of grayscale levels to encode. Valid range: 2-255.";
        colorsOption.DefaultValueFactory = _ => (byte)10;

        var fpsOption = new Option<double?>("--fps");
        fpsOption.Description = "Output FPS. Defaults to the source video FPS.";
        
        var overwriteOption = new Option<bool>("--overwrite");
        overwriteOption.Description = "Overwrite the output file if it already exists.";
        
        var presetOption = new Option<string?>("--preset");
        presetOption.Aliases.Add("-p");
        presetOption.Description = "Encoding speed and quality. " +
                                   "The slower the mode, the smaller the file size, but the longer the encoding will take." +
                                   " Presets: fastest, fast, balanced, slow, slowest";
        presetOption.DefaultValueFactory = _ => "balanced";

        var pFrameKOption = new Option<double?>("--pframe-k");
        pFrameKOption.Description = "P-frame threshold. Lower values create more keyframes.";
        pFrameKOption.DefaultValueFactory = _ => 0.1;

        var encodingModeOption = new Option<string?>("--encoding-mode");
        encodingModeOption.Description = "Frame encoding mode: fast, best-size, hybrid.";
        
        var brotliCompressionOption = new Option<string?>("--brotli-compression-mode");
        brotliCompressionOption.Description = "Brotli compression mode: slowest, optimal, fastest, no";

        convertCommand.Arguments.Add(inputArgument);
        convertCommand.Options.Add(outputOption);
        convertCommand.Options.Add(fpsOption);
        convertCommand.Options.Add(widthOption);
        convertCommand.Options.Add(heightOption);
        convertCommand.Options.Add(colorsOption);
        convertCommand.Options.Add(overwriteOption);
        convertCommand.Options.Add(presetOption);
        convertCommand.Options.Add(pFrameKOption);
        convertCommand.Options.Add(encodingModeOption);
        convertCommand.Options.Add(brotliCompressionOption);
        convertCommand.SetAction(parseResult =>
            ConvertHandler.Convert(
                parseResult.GetValue(inputArgument)!,
                parseResult.GetValue(outputOption),
                parseResult.GetValue(overwriteOption),
                parseResult.GetValue(widthOption),
                parseResult.GetValue(heightOption),
                parseResult.GetValue(colorsOption),
                parseResult.GetValue(fpsOption),
                parseResult.GetValue(presetOption),
                parseResult.GetValue(pFrameKOption),
                parseResult.GetValue(encodingModeOption),
                parseResult.GetValue(brotliCompressionOption)
                ));

        rootCommand.Subcommands.Add(playCommand);
        rootCommand.Subcommands.Add(convertCommand);

        return rootCommand.Parse(args).Invoke();
    }
}
