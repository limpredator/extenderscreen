import SwiftUI
import AVFoundation
import CoreMedia

/// Thin UIView wrapping AVSampleBufferDisplayLayer, which does the actual
/// (hardware-accelerated) H.264 decode + render.
final class VideoDisplayUIView: UIView {
    let displayLayer = AVSampleBufferDisplayLayer()

    override init(frame: CGRect) {
        super.init(frame: frame)
        displayLayer.videoGravity = .resizeAspect
        displayLayer.backgroundColor = UIColor.black.cgColor
        layer.addSublayer(displayLayer)
        backgroundColor = .black
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        displayLayer.frame = bounds
    }

    func enqueue(_ sampleBuffer: CMSampleBuffer) {
        if displayLayer.status == .failed {
            displayLayer.flush()
        }
        displayLayer.enqueue(sampleBuffer)
    }
}

struct VideoDisplayViewRepresentable: UIViewRepresentable {
    let view: VideoDisplayUIView

    func makeUIView(context: Context) -> VideoDisplayUIView { view }
    func updateUIView(_ uiView: VideoDisplayUIView, context: Context) {}
}
