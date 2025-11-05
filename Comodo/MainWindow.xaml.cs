using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Comodo
{
    public partial class MainWindow : Window
    {
        // Câmera em perspectiva usada no Viewport3D
        PerspectiveCamera camera;

        // Timer para atualizar animações (~60 FPS)
        DispatcherTimer temporizador;

        // Progresso da câmera (0..1) e das luzes (0..1)
        double tCamera = 0, tLuz = 0;

        // Controle do ping‑pong da câmera (ida/volta)
        bool indo = true;

        // Centro da casa (alvo da câmera) e raio do arco vertical da câmera
        Point3D centroCasa;
        double raioCamera;

        // Duas luzes direcionais (giram em sentidos opostos)
        DirectionalLight luz1, luz2;

        public MainWindow()
        {
            InitializeComponent();  // carrega o XAML (Viewport3D: Name="MainViewport")
            MontarCena();           // constrói a cena e inicia animações
        }

        void MontarCena()
        {
            // Dimensões base dos cômodos e do telhado
            double largura = 4, altura = 2.5, profundidade = 4, separacao = 0.06, alturaTelhado = 1.8;

            // Grupo raiz dos modelos 3D
            var cena = new Model3DGroup();

            // Luz ambiente fraca (base da iluminação)
            cena.Children.Add(new AmbientLight(Color.FromScRgb(1, .25f, .25f, .25f)));

            // Duas luzes direcionais brancas, opostas, com 30° de inclinação para baixo
            var brancoMedio = Color.FromScRgb(1, .85f, .85f, .85f);
            luz1 = new DirectionalLight(brancoMedio, new Vector3D(-0.866, -0.5, 0));
            luz2 = new DirectionalLight(brancoMedio, new Vector3D(0.866, -0.5, 0));
            cena.Children.Add(luz1);
            cena.Children.Add(luz2);

            // Materiais sólidos (cores distintas) para cômodos e telhado
            Material matComodo1 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(135, 206, 235)));
            Material matComodo2 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(144, 238, 144)));
            Material matComodo3 = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(250, 128, 114)));
            Material matTelhado = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(139, 69, 19)));

            // Três cômodos (3 caixas lado a lado)
            for (int i = 0; i < 3; i++)
            {
                var origem = new Point3D(i * (largura + separacao), 0, 0);
                var malha = CriarCaixa(origem, largura, altura, profundidade);
                var mat = i == 0 ? matComodo1 : i == 1 ? matComodo2 : matComodo3;
                cena.Children.Add(new GeometryModel3D { Geometry = malha, Material = mat, BackMaterial = mat });
            }

            // Comprimento total (para cobrir com o telhado)
            double comprimentoTotal = 3 * largura + 2 * separacao;

            // Alturas do telhado (base e cumeeira)
            double yBase = altura, yCumeeira = altura + alturaTelhado;

            // Telhado em duas águas + fechamento das empenas
            var malhaTelhado = CriarTelhado(comprimentoTotal, profundidade, yBase, yCumeeira);
            cena.Children.Add(new GeometryModel3D { Geometry = malhaTelhado, Material = matTelhado, BackMaterial = matTelhado });

            // Chão plano grande o suficiente para a visão da câmera
            double tamChao = Math.Max(comprimentoTotal, profundidade) * 3.5;
            var malhaChao = CriarPlano(new Point3D(comprimentoTotal / 2.0 - tamChao / 2.0, -0.01, profundidade / 2.0 - tamChao / 2.0), tamChao, tamChao);

            // Textura de pedra repetida (sem fallback): exige Resources/stone.png como Resource
            var matChao = PedraRepetida("Resources/stone.png", 12);
            cena.Children.Add(new GeometryModel3D { Geometry = malhaChao, Material = matChao, BackMaterial = matChao });

            // Injeta a cena no Viewport3D definido no XAML
            MainViewport.Children.Clear();
            MainViewport.Children.Add(new ModelVisual3D { Content = cena });

            // Centro da casa (alvo da câmera) e raio do arco da câmera
            centroCasa = new Point3D(comprimentoTotal / 2.0, altura / 2.0, profundidade / 2.0);
            raioCamera = Math.Max(Math.Max(comprimentoTotal, profundidade), altura + alturaTelhado) * 2.0;

            // Câmera perspectiva (FOV 45°) controlada por código
            camera = new PerspectiveCamera { FieldOfView = 45 };
            MainViewport.Camera = camera;
            AtualizarCamera();

            // Temporizador de animação (~60 FPS)
            temporizador = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            temporizador.Tick += (_, __) => Tick();
            temporizador.Start();
        }

        void Tick()
        {
            double dt = 0.016; // passo fixo (~60 FPS)

            // Câmera: arco de 180° em 5 s (ping‑pong)
            double velCam = dt / 5.0; // 1.0 em 5 s
            tCamera += (indo ? +velCam : -velCam);
            if (tCamera >= 1) { tCamera = 1; indo = false; }  // topo (180°) → volta
            if (tCamera <= 0) { tCamera = 0; indo = true; }  // base (0°) → ida
            AtualizarCamera();

            // Luzes: volta completa (360°) em 10 s, loop contínuo
            tLuz += dt / 10.0;     // 1.0 em 10 s
            if (tLuz >= 1) tLuz -= Math.Floor(tLuz);
            AtualizarLuzes();
        }

        void AtualizarCamera()
        {
            // tCamera (0..1) → ângulo θ (0..π) para arco vertical de 180°
            double theta = tCamera * Math.PI;

            // Posição em arco vertical no plano X‑Y (z fixo no centro da casa)
            double x = centroCasa.X + raioCamera * Math.Cos(theta);
            double y = centroCasa.Y + raioCamera * Math.Sin(theta);
            double z = centroCasa.Z;

            var pos = new Point3D(x, y, z);
            camera.Position = pos;                           // coloca a câmera no arco
            camera.LookDirection = (Vector3D)(centroCasa - pos); // aponta para o centro da casa
            camera.UpDirection = new Vector3D(0, 1, 0);        // “cima” global (simplificado)
        }

        void AtualizarLuzes()
        {
            // Ângulo φ (0..2π) para rotação horizontal completa
            double phi = tLuz * 2 * Math.PI;

            // Inclinação de 30° para baixo
            double elev = 30 * Math.PI / 180.0;
            double senE = Math.Sin(elev), cosE = Math.Cos(elev);

            // Luz 1 gira com φ
            luz1.Direction = new Vector3D(cosE * Math.Cos(phi), -senE, cosE * Math.Sin(phi));

            // Luz 2 gira ao contrário (oposta): φ2 = π − φ
            double phi2 = Math.PI - phi;
            luz2.Direction = new Vector3D(cosE * Math.Cos(phi2), -senE, cosE * Math.Sin(phi2));
        }

        // Cria um plano horizontal (Y constante), com UVs 0..1
        static MeshGeometry3D CriarPlano(Point3D origem, double largura, double profundidade)
        {
            var m = new MeshGeometry3D();
            var p0 = origem;
            var p1 = new Point3D(origem.X + largura, origem.Y, origem.Z);
            var p2 = new Point3D(origem.X + largura, origem.Y, origem.Z + profundidade);
            var p3 = new Point3D(origem.X, origem.Y, origem.Z + profundidade);

            m.Positions = new Point3DCollection { p0, p1, p2, p3 };
            var up = new Vector3D(0, 1, 0);
            m.Normals = new Vector3DCollection { up, up, up, up };
            m.TextureCoordinates = new PointCollection{
        new System.Windows.Point(0,0), new System.Windows.Point(1,0),
        new System.Windows.Point(1,1), new System.Windows.Point(0,1)
      };
            m.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 };
            return m;
        }

        // Material de pedra em tile (sem fallback): requer Resources/stone.png como Resource
        static Material PedraRepetida(string caminhoRelativo, int repeticoesPorLado = 12)
        {
            var uri = new Uri($"pack://application:,,,/{caminhoRelativo}", UriKind.Absolute);
            var bmp = new BitmapImage(uri);

            double s = 1.0 / Math.Max(1, repeticoesPorLado); // tamanho da “telha” em coordenada relativa
            var pincel = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, s, s),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.UniformToFill
            };
            return new DiffuseMaterial(pincel);
        }

        // Caixa com 6 faces (cada face como quad com normal por produto vetorial)
        static MeshGeometry3D CriarCaixa(Point3D o, double L, double A, double P)
        {
            var m = new MeshGeometry3D();

            void Face(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                int i = m.Positions.Count;
                m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(d);
                var n = Vector3D.CrossProduct(b - a, d - a); n.Normalize();
                m.Normals.Add(n); m.Normals.Add(n); m.Normals.Add(n); m.Normals.Add(n);
                m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
                m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
            }

            double x = o.X, y = o.Y, z = o.Z;
            var p000 = new Point3D(x, y, z);
            var p100 = new Point3D(x + L, y, z);
            var p010 = new Point3D(x, y + A, z);
            var p110 = new Point3D(x + L, y + A, z);
            var p001 = new Point3D(x, y, z + P);
            var p101 = new Point3D(x + L, y, z + P);
            var p011 = new Point3D(x, y + A, z + P);
            var p111 = new Point3D(x + L, y + A, z + P);

            Face(p100, p000, p010, p110); // frente
            Face(p001, p101, p111, p011); // trás
            Face(p000, p001, p011, p010); // esquerda
            Face(p101, p100, p110, p111); // direita
            Face(p110, p010, p011, p111); // topo
            Face(p000, p100, p101, p001); // base
            return m;
        }

        // Telhado em duas águas + empenas triangulares
        static MeshGeometry3D CriarTelhado(double comp, double prof, double yBase, double yCumeeira)
        {
            var m = new MeshGeometry3D();

            void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                int i = m.Positions.Count;
                m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(d);
                var n = Vector3D.CrossProduct(b - a, d - a); n.Normalize();
                m.Normals.Add(n); m.Normals.Add(n); m.Normals.Add(n); m.Normals.Add(n);
                m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
                m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
            }

            void Tri(Point3D a, Point3D b, Point3D c)
            {
                int i = m.Positions.Count;
                m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c);
                var n = Vector3D.CrossProduct(b - a, c - a); n.Normalize();
                m.Normals.Add(n); m.Normals.Add(n); m.Normals.Add(n);
                m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
            }

            var a0 = new Point3D(0, yBase, 0);
            var a1 = new Point3D(comp, yBase, 0);
            var a2 = new Point3D(comp, yCumeeira, prof / 2);
            var a3 = new Point3D(0, yCumeeira, prof / 2);

            var b0 = new Point3D(0, yBase, prof);
            var b1 = new Point3D(0, yCumeeira, prof / 2);
            var b2 = new Point3D(comp, yCumeeira, prof / 2);
            var b3 = new Point3D(comp, yBase, prof);

            Quad(a0, a1, a2, a3); // água frontal
            Quad(b0, b1, b2, b3); // água traseira

            Tri(a0, a1, a2); Tri(a0, a2, a3); // empena frontal
            Tri(b0, b3, b2); Tri(b0, b2, b1); // empena traseira

            return m;
        }
    }
}
