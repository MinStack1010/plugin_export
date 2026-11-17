"""Small logger writing to a log file and optional extra sinks (NX syslog)."""

import datetime
import os
import tempfile


class ExportLogger:
    def __init__(self, directory="", file_name="synode_nx_export.log", sinks=()):
        self.path = None
        self._sinks = list(sinks)
        self.warnings = []
        try:
            directory = directory or os.path.join(tempfile.gettempdir(), "SynodeNX")
            os.makedirs(directory, exist_ok=True)
            self.path = os.path.join(directory, file_name)
        except OSError:
            self.path = None

    def add_sink(self, sink):
        self._sinks.append(sink)

    def _write(self, level, message):
        line = "{} [{}] {}".format(datetime.datetime.now().isoformat(timespec="seconds"), level, message)
        if self.path:
            try:
                with open(self.path, "a", encoding="utf-8") as handle:
                    handle.write(line + "\n")
            except OSError:
                pass
        for sink in self._sinks:
            try:
                sink(line)
            except Exception:  # never let logging break an export
                pass

    def info(self, message):
        self._write("INFO", message)

    def warning(self, message):
        self.warnings.append(message)
        self._write("WARN", message)

    def error(self, message):
        self._write("ERROR", message)
