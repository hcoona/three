import java.nio.file.Files;
import java.nio.file.Path;
import tla2sany.drivers.FrontEndException;
import tla2sany.drivers.SANY;
import tla2sany.modanalyzer.SpecObj;
import util.SimpleFilenameToStream;
import util.ToolIO;

/**
 * Batch SANY syntax, semantic, and level checker for TLA+ modules.
 *
 * <p>Upstream {@code tla2sany.SANY} in {@code tla2tools.jar} v1.7.4 exits 0 on non-aborting
 * semantic errors and resolves sibling {@code EXTENDS}/{@code INSTANCE} modules relative to the
 * process working directory rather than each target module's parent directory. This entry point
 * isolates each checked file's local module search path and exits non-zero on any init, parse,
 * global-context, semantic, or level-checking error.
 */
public final class TlaSanyCheck {
  private TlaSanyCheck() {}

  public static void main(String[] args) {
    System.clearProperty(SimpleFilenameToStream.TLA_LIBRARY);
    boolean failed = false;
    boolean literalPaths = false;
    int checked = 0;

    for (String arg : args) {
      if (!literalPaths && "--".equals(arg)) {
        literalPaths = true;
        continue;
      }

      Path file = Path.of(arg).toAbsolutePath().normalize();
      if (!Files.isRegularFile(file)) {
        System.err.println("SANY: file not found: " + arg);
        failed = true;
        continue;
      }

      checked++;
      Path parent = file.getParent();
      String moduleFile = file.getFileName().toString();

      ToolIO.reset();
      ToolIO.setUserDir(parent != null ? parent.toString() : null);
      SimpleFilenameToStream resolver = new SimpleFilenameToStream();
      ToolIO.setDefaultResolver(resolver);
      SpecObj spec = new SpecObj(moduleFile, resolver);
      try {
        int status = SANY.frontEndMain(spec, moduleFile, System.out);
        if (status != 0
            || spec.getErrorLevel() != 0
            || !spec.getInitErrors().isSuccess()
            || !spec.getParseErrors().isSuccess()
            || (spec.getGlobalContextErrors() != null
                && !spec.getGlobalContextErrors().isSuccess())
            || !spec.getSemanticErrors().isSuccess()) {
          failed = true;
        }
      } catch (FrontEndException exception) {
        exception.printStackTrace(System.err);
        failed = true;
      }
    }

    if (checked == 0 && !failed) {
      System.err.println("SANY: at least one .tla file is required.");
      System.exit(2);
    }
    System.exit(failed ? 1 : 0);
  }
}
