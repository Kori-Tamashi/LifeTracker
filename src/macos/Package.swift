// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "LifeTrackerMac",
    platforms: [.macOS("26.0")],
    products: [
        .library(name: "LifeTrackerClient", targets: ["LifeTrackerClient"]),
        .executable(name: "LifeTrackerDesktop", targets: ["LifeTrackerDesktop"]),
    ],
    targets: [
        .target(name: "LifeTrackerClient"),
        .executableTarget(name: "LifeTrackerDesktop", dependencies: ["LifeTrackerClient"]),
        .testTarget(name: "LifeTrackerClientTests", dependencies: ["LifeTrackerClient"]),
    ]
)
