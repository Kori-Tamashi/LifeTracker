// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "LifeTrackerMac",
    platforms: [.macOS("26.0")],
    products: [.library(name: "LifeTrackerClient", targets: ["LifeTrackerClient"])],
    targets: [
        .target(name: "LifeTrackerClient"),
        .testTarget(name: "LifeTrackerClientTests", dependencies: ["LifeTrackerClient"])
    ]
)
