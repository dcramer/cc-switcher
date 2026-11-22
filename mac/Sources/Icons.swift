import AppKit

/// Claude's icon recolored to a profile's color.
enum Icons {
    private static var cache: [String: NSImage] = [:]

    static func color(_ hex: String) -> NSColor {
        var value: UInt64 = 0
        Scanner(string: hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))).scanHexInt64(&value)
        return NSColor(srgbRed: CGFloat((value >> 16) & 0xFF) / 255,
                       green: CGFloat((value >> 8) & 0xFF) / 255,
                       blue: CGFloat(value & 0xFF) / 255, alpha: 1)
    }

    static func profileIcon(_ hex: String, claude: URL?, pixels: CGFloat = 128) -> NSImage {
        let key = "\(hex)@\(pixels)"
        if let cached = cache[key] { return cached }
        let size = NSSize(width: pixels, height: pixels)
        let rect = NSRect(origin: .zero, size: size)
        let base: NSImage = claude.map { NSWorkspace.shared.icon(forFile: $0.path) }
            ?? NSImage(systemSymbolName: "sparkle", accessibilityDescription: nil) ?? NSImage(size: size)

        // The icon's silhouette in the profile color, laid over it with the Color blend mode:
        // the profile's hue with the icon's own shading.
        let silhouette = NSImage(size: size, flipped: false) { _ in
            base.draw(in: rect)
            color(hex).set()
            rect.fill(using: .sourceIn)
            return true
        }
        let tinted = NSImage(size: size, flipped: false) { _ in
            base.draw(in: rect)
            silhouette.draw(in: rect, from: .zero, operation: .color, fraction: 1)
            return true
        }
        cache[key] = tinted
        return tinted
    }
}
