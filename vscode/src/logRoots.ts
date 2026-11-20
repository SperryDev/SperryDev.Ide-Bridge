const EXTHOST_SEGMENT = /^(.*[\\/]exthost)(?:[\\/]|$)/i;

/**
 * The exthost folder inside a window's log folder, derived from the extension's log path.
 * Channel files live beside it in output_logging_<ts>/ folders. Returns undefined when the path has no
 * exthost segment, or when the window is remote: the logs then sit on the remote host, not this machine.
 */
export function exthostLogRoot(extensionLogPath: string, remoteName: string | undefined): string | undefined {
    if (remoteName !== undefined && remoteName !== "") {
        return undefined;
    }

    const match = EXTHOST_SEGMENT.exec(extensionLogPath);
    return match ? match[1] : undefined;
}
