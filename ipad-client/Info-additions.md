# Info.plist additions

Xcode's SwiftUI App template generates `Info.plist` entries for you — you
don't edit a separate file. Add this one key via **Project navigator → your
target → Info tab → "+"** (or open the raw Info.plist and add it as XML):

| Key | Type | Value |
|---|---|---|
| `Privacy - Local Network Usage Description` (`NSLocalNetworkUsageDescription`) | String | `ExtendedScreen connects to the ExtendedScreen sender running on your computer over your local Wi-Fi network.` |

That's the only entry needed for v1 (plain TCP to a manually-entered IP). If
Bonjour auto-discovery is added later, `NSBonjourServices` (listing
`_extscreen._tcp`) would need to be added too.

Raw XML form, if you're editing Info.plist as source:

```xml
<key>NSLocalNetworkUsageDescription</key>
<string>ExtendedScreen connects to the ExtendedScreen sender running on your computer over your local Wi-Fi network.</string>
```

## Orientation (portrait, landscape, both flipped)

Xcode's new-project wizard defaults iPad apps to supporting all four
orientations already, but it's worth confirming: **target → General →
Deployment Info → Supported interface orientations** should have all four
boxes checked (Portrait, Upside Down, Landscape Left, Landscape Right). No
code changes needed beyond that — `VideoDisplayUIView.layoutSubviews()`
already re-sizes `displayLayer.frame` to match whatever the current bounds
are, so the video just reflows on rotation.
