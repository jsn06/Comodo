using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Comodo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Animation fields
        private PerspectiveCamera animatedCamera;
        private DispatcherTimer animationTimer;
        private Stopwatch stopwatch;
        private long lastMs;
        private double t; // progress 0..1 for a single 180° arc (camera)
        private bool forward = true;
        private const double arcDurationSeconds = 5.0; // each 180° arc duration (camera)

        // Camera path parameters computed after scene creation
        private Point3D houseCenter;
        private double cameraRadius;

        // Light animation fields
        private DirectionalLight dirLight1;
        private DirectionalLight dirLight2;
        private double lightT; // 0..1 representing full 360° rotation
        private const double lightRotationDuration = 10.0; // seconds per full 360°

        public MainWindow()
        {
            InitializeComponent();
            BuildHouseScene();
        }

        private void BuildHouseScene()
        {
            // Parameters
            double roomWidth = 4.0;
            double roomHeight = 2.5;
            double roomDepth = 4.0;
            int roomCount = 3;
            double roofHeight = 1.8;
            double gap = 0.06; // small separation so rooms don't perfectly merge

            // Model group for the whole scene
            var scene = new Model3DGroup();

            // Ambient base light
            scene.Children.Add(new AmbientLight(Color.FromScRgb(1.0f, 0.25f, 0.25f, 0.25f)));

            // Create two directional lights (white, medium intensity), initial directions opposite and 30° elevation.
            // Use FromScRgb to set slightly reduced intensity (scRGB channels < 1.0).
            var lightColor = Color.FromScRgb(1.0f, 0.85f, 0.85f, 0.85f); // medium-white

            dirLight1 = new DirectionalLight(lightColor, new Vector3D(-0.866, -0.5, 0.0)); // initial pointing at +X side (30° down)
            dirLight2 = new DirectionalLight(lightColor, new Vector3D(0.866, -0.5, 0.0));  // opposite direction

            scene.Children.Add(dirLight1);
            scene.Children.Add(dirLight2);

            // Materials (DiffuseMaterial required by the spec)
            var materialRoom1 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(135, 206, 235))); // SkyBlue
            var materialRoom2 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(144, 238, 144))); // LightGreen
            var materialRoom3 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(250, 128, 114))); // Salmon (orange-ish)
            var materialRoof = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(139, 69, 19))); // SaddleBrown

            // Create rooms (adjacent boxes), add a small gap between them
            for (int i = 0; i < roomCount; i++)
            {
                var origin = new Point3D(i * (roomWidth + gap), 0, 0);
                var mesh = CreateBoxMesh(origin, roomWidth, roomHeight, roomDepth);

                Material material = i switch
                {
                    0 => materialRoom1,
                    1 => materialRoom2,
                    _ => materialRoom3
                };

                var model = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = material,
                    BackMaterial = material
                };

                scene.Children.Add(model);
            }

            // Compute total length based on gaps
            double totalLength = roomCount * roomWidth + (roomCount - 1) * gap;

            // Create gabled roof covering all rooms.
            double roofBaseY = roomHeight - 0.005;             // tiny sink to avoid z-fighting with boxes' tops
            double roofRidgeY = roomHeight + roofHeight + 0.01; // tiny lift
            var roofMesh = CreateGabledRoofMesh(totalLength, roomDepth, roofBaseY, roofRidgeY);
            var roofModel = new GeometryModel3D
            {
                Geometry = roofMesh,
                Material = materialRoof,
                BackMaterial = materialRoof
            };
            scene.Children.Add(roofModel);

            // --- Textured flat ground plane ---
            double groundSize = Math.Max(totalLength, roomDepth) * 8.0; // generous coverage
            double groundY = -0.01;
            double centerX = totalLength / 2.0;
            double centerZ = roomDepth / 2.0;
            var groundOrigin = new Point3D(centerX - groundSize / 2.0, groundY, centerZ - groundSize / 2.0);

            var groundMesh = CreatePlaneMesh(groundOrigin, groundSize, groundSize);
            Material groundMaterial = CreateTiledStoneMaterial("Resources/stone.jpg", tilesPerSide: 16);
            var groundModel = new GeometryModel3D
            {
                Geometry = groundMesh,
                Material = groundMaterial,
                BackMaterial = groundMaterial
            };
            scene.Children.Add(groundModel);

            // Put the scene into the viewport
            var modelVisual = new ModelVisual3D { Content = scene };
            MainViewport.Children.Clear();
            MainViewport.Children.Add(modelVisual);

            // Compute house center and camera radius to keep the house fully visible
            houseCenter = new Point3D(centerX, roomHeight / 2.0, centerZ); // center Y uses mid-height of rooms
            // radius chosen to comfortably fit the house in view while allowing arc over the roof
            cameraRadius = Math.Max(Math.Max(totalLength, roomDepth), roomHeight + roofHeight) * 2.2;

            // Create and assign animated PerspectiveCamera
            animatedCamera = new PerspectiveCamera
            {
                FieldOfView = 45.0
            };
            MainViewport.Camera = animatedCamera;

            // Initialize animation state: start at one side at house level, looking horizontally
            t = 0.0; // maps to theta = 0 -> start on one side at house level
            UpdateAnimatedCamera(); // set initial camera transform

            // Initialize light animation parameter
            lightT = 0.0;

            // Setup timer + stopwatch for smooth ping-pong animation (camera) and continuous light rotation
            stopwatch = Stopwatch.StartNew();
            lastMs = stopwatch.ElapsedMilliseconds;
            animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            animationTimer.Tick += AnimationTimer_Tick;
            animationTimer.Start();
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            long now = stopwatch.ElapsedMilliseconds;
            double dt = (now - lastMs) / 1000.0;
            lastMs = now;

            // Camera ping-pong 180° arcs (existing behavior)
            if (forward)
            {
                t += dt / arcDurationSeconds;
                if (t >= 1.0)
                {
                    t = 1.0;
                    forward = false;
                }
            }
            else
            {
                t -= dt / arcDurationSeconds;
                if (t <= 0.0)
                {
                    t = 0.0;
                    forward = true;
                }
            }
            UpdateAnimatedCamera();

            // Lights continuous rotation: full 360° in lightRotationDuration seconds
            lightT += dt / lightRotationDuration;
            // keep in [0,1)
            if (lightT >= 1.0) lightT -= Math.Floor(lightT);

            UpdateLights(lightT);
        }

        // Map t [0..1] to theta [0..pi] (180°) and update camera position/look/up
        private void UpdateAnimatedCamera()
        {
            // theta: 0 => one side at house level (horizontal look); pi => opposite side at house level
            double theta = t * Math.PI;
            double x = houseCenter.X + cameraRadius * Math.Cos(theta);
            double y = houseCenter.Y + cameraRadius * Math.Sin(theta);
            double z = houseCenter.Z; // fixed so arc passes centrally over the house

            var camPos = new Point3D(x, y, z);
            var lookDirection = (Vector3D)(houseCenter - camPos);

            // Choose an UpDirection that is not (anti)parallel to lookDirection
            var globalUp = new Vector3D(0, 1, 0);
            Vector3D upDir = globalUp;
            var lookNorm = lookDirection;
            lookNorm.Normalize();
            if (Math.Abs(Vector3D.DotProduct(lookNorm, globalUp)) > 0.99)
            {
                // near vertical look -> use Z axis as up to avoid singularity
                upDir = new Vector3D(0, 0, 1);
            }

            animatedCamera.Position = camPos;           
            animatedCamera.LookDirection = lookDirection;
            animatedCamera.UpDirection = upDir;
        }

        // Update the two directional lights according to a normalized progress (0..1)
        private void UpdateLights(double progress)
        {
            // progress -> phi in radians [0, 2π)
            double phi = progress * Math.PI * 2.0;
            // Elevation above horizon: 30 degrees -> light points downward (y negative)
            double elevRad = 30.0 * Math.PI / 180.0;
            double sinE = Math.Sin(elevRad);    // 0.5
            double cosE = Math.Cos(elevRad);    // ~0.866

            // Light 1 rotates with phi
            double lx1 = cosE * Math.Cos(phi);
            double lz1 = cosE * Math.Sin(phi);
            double ly1 = -sinE; // points downward

            // Light 2 rotates in opposite direction and is always ~opposite-facing initially:
            // use phi2 = π - phi so at phi=0 it is opposite to light1, and as phi increases it rotates opposite.
            double phi2 = Math.PI - phi;
            double lx2 = cosE * Math.Cos(phi2);
            double lz2 = cosE * Math.Sin(phi2);
            double ly2 = -sinE;

            dirLight1.Direction = new Vector3D(lx1, ly1, lz1);
            dirLight2.Direction = new Vector3D(lx2, ly2, lz2);
        }

        // Create a large flat plane (single quad) with normals up and basic texture coords 0..1
        private MeshGeometry3D CreatePlaneMesh(Point3D origin, double width, double depth)
        {
            var m = new MeshGeometry3D();

            var p0 = origin;
            var p1 = new Point3D(origin.X + width, origin.Y, origin.Z);
            var p2 = new Point3D(origin.X + width, origin.Y, origin.Z + depth);
            var p3 = new Point3D(origin.X, origin.Y, origin.Z + depth);

            m.Positions.Add(p0);
            m.Positions.Add(p1);
            m.Positions.Add(p2);
            m.Positions.Add(p3);

            // single normal (up) for all vertices
            var up = new Vector3D(0, 1, 0);
            m.Normals.Add(up);
            m.Normals.Add(up);
            m.Normals.Add(up);
            m.Normals.Add(up);

            // Texture coordinates cover whole quad; the brush will tile using Viewport
            m.TextureCoordinates.Add(new System.Windows.Point(0, 0));
            m.TextureCoordinates.Add(new System.Windows.Point(1, 0));
            m.TextureCoordinates.Add(new System.Windows.Point(1, 1));
            m.TextureCoordinates.Add(new System.Windows.Point(0, 1));

            // two triangles
            m.TriangleIndices.Add(0);
            m.TriangleIndices.Add(1);
            m.TriangleIndices.Add(2);

            m.TriangleIndices.Add(0);
            m.TriangleIndices.Add(2);
            m.TriangleIndices.Add(3);

            return m;
        }

        // Attempts to create a DiffuseMaterial that tiles the provided image across the plane.
        // imageRelativePath: path relative to project root (e.g. "Resources/stone.jpg") added as Resource.
        // tilesPerSide: how many times the image should repeat across the plane (approx).
        private Material CreateTiledStoneMaterial(string imageRelativePath, int tilesPerSide = 8)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{imageRelativePath}", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);

                double tileSize = 1.0 / Math.Max(1, tilesPerSide);
                var imgBrush = new ImageBrush(bitmap)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, tileSize, tileSize),
                    ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.UniformToFill
                };

                return new DiffuseMaterial(imgBrush);
            }
            catch (Exception)
            {
                // Fallback: simple gray material if texture is missing
                return new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(200, 200, 200)));
            }
        }

        // Build a box mesh (six faces). Normals provided per face.
        private MeshGeometry3D CreateBoxMesh(Point3D origin, double width, double height, double depth)
        {
            var m = new MeshGeometry3D();

            void AddFace(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
            {
                int index = m.Positions.Count;
                m.Positions.Add(p0);
                m.Positions.Add(p1);
                m.Positions.Add(p2);
                m.Positions.Add(p3);

                m.Normals.Add(normal);
                m.Normals.Add(normal);
                m.Normals.Add(normal);
                m.Normals.Add(normal);

                // two triangles (0,1,2) and (0,2,3)
                m.TriangleIndices.Add(index + 0);
                m.TriangleIndices.Add(index + 1);
                m.TriangleIndices.Add(index + 2);

                m.TriangleIndices.Add(index + 0);
                m.TriangleIndices.Add(index + 2);
                m.TriangleIndices.Add(index + 3);
            }

            double x = origin.X;
            double y = origin.Y;
            double z = origin.Z;

            // corners
            var p000 = new Point3D(x, y, z);
            var p100 = new Point3D(x + width, y, z);
            var p010 = new Point3D(x, y + height, z);
            var p110 = new Point3D(x + width, y + height, z);

            var p001 = new Point3D(x, y, z + depth);
            var p101 = new Point3D(x + width, y, z + depth);
            var p011 = new Point3D(x, y + height, z + depth);
            var p111 = new Point3D(x + width, y + height, z + depth);

            // Front (z)
            AddFace(p100, p000, p010, p110, new Vector3D(0, 0, -1));
            // Back
            AddFace(p001, p101, p111, p011, new Vector3D(0, 0, 1));
            // Left
            AddFace(p000, p001, p011, p010, new Vector3D(-1, 0, 0));
            // Right
            AddFace(p101, p100, p110, p111, new Vector3D(1, 0, 0));
            // Top
            AddFace(p110, p010, p011, p111, new Vector3D(0, 1, 0));
            // Bottom
            AddFace(p000, p100, p101, p001, new Vector3D(0, -1, 0));

            return m;
        }

        // Create a gabled roof from two sloped quads. This overload takes explicit baseY and ridgeY
        private MeshGeometry3D CreateGabledRoofMesh(double length, double depth, double baseY, double ridgeY)
        {
            var m = new MeshGeometry3D();

            // Left roof plane (facing -Z)
            var a0 = new Point3D(0, baseY, 0);
            var a1 = new Point3D(length, baseY, 0);
            var a2 = new Point3D(length, ridgeY, depth / 2.0);
            var a3 = new Point3D(0, ridgeY, depth / 2.0);

            // Right roof plane (facing +Z)
            var b0 = new Point3D(0, baseY, depth);
            var b1 = new Point3D(0, ridgeY, depth / 2.0);
            var b2 = new Point3D(length, ridgeY, depth / 2.0);
            var b3 = new Point3D(length, baseY, depth);

            void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
            {
                int idx = m.Positions.Count;
                m.Positions.Add(p0);
                m.Positions.Add(p1);
                m.Positions.Add(p2);
                m.Positions.Add(p3);

                m.Normals.Add(normal);
                m.Normals.Add(normal);
                m.Normals.Add(normal);
                m.Normals.Add(normal);

                m.TriangleIndices.Add(idx + 0);
                m.TriangleIndices.Add(idx + 1);
                m.TriangleIndices.Add(idx + 2);

                m.TriangleIndices.Add(idx + 0);
                m.TriangleIndices.Add(idx + 2);
                m.TriangleIndices.Add(idx + 3);
            }

            var leftNormal = Vector3D.CrossProduct(a1 - a0, a3 - a0);
            leftNormal.Normalize();
            var rightNormal = Vector3D.CrossProduct(b1 - b0, b3 - b0);
            rightNormal.Normalize();

            AddQuad(a0, a1, a2, a3, leftNormal);
            AddQuad(b0, b1, b2, b3, rightNormal);

            return m;
        }
    }
}