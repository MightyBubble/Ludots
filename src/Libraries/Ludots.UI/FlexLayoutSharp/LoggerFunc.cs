namespace FlexLayoutSharp;

public delegate int LoggerFunc(Config config, Node node, LogLevel level, string format, params object[] args);
