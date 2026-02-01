using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class Blob : MonoBehaviour
{
    public enum ScanlineShape
    {
        Cube,
        Cylinder
    }

    #region Variables

    public const int MaxSize = 64;

    [Header("Voxel Grid")]
    public Vector3Int size = new Vector3Int(16, 16, 16);
    public float voxelSize = 1f;
    [Range(0f, 1f)]
    public float isoLevel = 0.25f;

    [Header("Generation")]
    public bool generateOnStart = true;
    public bool fillSolidOnStart = true;
    public bool regenerateOnValidate = true;
    public Color fillColor = Color.white;
    public bool useGradientNormals = false;

    [Header("Scanline")]
    public float scanlineLayersPerSecond = 2f;
    public ScanlineShape scanlineShape = ScanlineShape.Cylinder;
    [Range(0f, 1f)]
    public float scanlineShapeScaleX = 1f;
    [Range(0f, 1f)]
    public float scanlineShapeScaleZ = 1f;
    [Range(-180f, 180f)]
    public float scanlineShapeRotationDegrees = 0f;
    public Transform scanlineCubePreview;
    public Transform scanlineCylinderPreview;

    [Header("Scanline Input")]
    public float scanlineMouseScaleSensitivity = 0.002f;
    public float scanlineScrollScaleSensitivity = 0.001f;
    public float scanlinePauseBudgetSeconds = 5f;

    [Header("Scan Grid")]
    public float scanDistanceMax = 2f;
    public GameObject scanTarget;
    public bool scanUseMeshDistance = false;
    public bool scanUseSurfaceVoxelization = false;
    public bool scanUseRaycastToCenter = false;
    [Min(0)]
    public int scanSurfaceSealVoxels = 0;
    [Range(0.05f, 1f)]
    public float scanSurfaceThickness = 0.25f;
    public bool scanUseParallel = true;

    private float[,,] voxels;
    private Color[,,] colors;
    private float[,,] scanVoxels;
    private Color[,,] scanColors;
    private Mesh cachedScanMesh;
    private MeshAccel cachedScanAccel;
    private Vector3[] cachedScanVertices;
    private int[] cachedScanTriangles;
    private Vector3[] cachedScanWorldVertices;
    private Bounds cachedScanWorldBounds;
    private float cachedScanVoxelSize = -1f;
    private Vector3 cachedScanTargetPosition;
    private Quaternion cachedScanTargetRotation;
    private Vector3 cachedScanTargetScale;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private Coroutine scanlineRoutine;
    private int scanlineCurrentLayer = -1;
    private Vector3Int lastValidatedSize;
    private float lastValidatedVoxelSize = -1f;
    private float lastValidatedIsoLevel = -1f;
    private InputSystem_Actions inputActions;
    private bool isRightMouseHeld;
    private bool scanlinePendingStart;
    private bool scanlineBudgetInitialized;
    private float pauseBudgetRemaining;

    #endregion

    public bool IsScanlineActive => scanlineRoutine != null;

    #region Matixes
    private static readonly int[,] VertexOffset =
    {
        {0, 0, 0}, {1, 0, 0}, {1, 1, 0}, {0, 1, 0},
        {0, 0, 1}, {1, 0, 1}, {1, 1, 1}, {0, 1, 1}
    };

    private static readonly int[,] EdgeConnection =
    {
        {0, 1}, {1, 2}, {2, 3}, {3, 0},
        {4, 5}, {5, 6}, {6, 7}, {7, 4},
        {0, 4}, {1, 5}, {2, 6}, {3, 7}
    };

    private static readonly int[] CubeEdgeFlags =
    {
        0x000, 0x109, 0x203, 0x30a, 0x406, 0x50f, 0x605, 0x70c, 0x80c, 0x905, 0xa0f, 0xb06, 0xc0a, 0xd03, 0xe09, 0xf00,
        0x190, 0x099, 0x393, 0x29a, 0x596, 0x49f, 0x795, 0x69c, 0x99c, 0x895, 0xb9f, 0xa96, 0xd9a, 0xc93, 0xf99, 0xe90,
        0x230, 0x339, 0x033, 0x13a, 0x636, 0x73f, 0x435, 0x53c, 0xa3c, 0xb35, 0x83f, 0x936, 0xe3a, 0xf33, 0xc39, 0xd30,
        0x3a0, 0x2a9, 0x1a3, 0x0aa, 0x7a6, 0x6af, 0x5a5, 0x4ac, 0xbac, 0xaa5, 0x9af, 0x8a6, 0xfaa, 0xea3, 0xda9, 0xca0,
        0x460, 0x569, 0x663, 0x76a, 0x066, 0x16f, 0x265, 0x36c, 0xc6c, 0xd65, 0xe6f, 0xf66, 0x86a, 0x963, 0xa69, 0xb60,
        0x5f0, 0x4f9, 0x7f3, 0x6fa, 0x1f6, 0x0ff, 0x3f5, 0x2fc, 0xdfc, 0xcf5, 0xfff, 0xef6, 0x9fa, 0x8f3, 0xbf9, 0xaf0,
        0x650, 0x759, 0x453, 0x55a, 0x256, 0x35f, 0x055, 0x15c, 0xe5c, 0xf55, 0xc5f, 0xd56, 0xa5a, 0xb53, 0x859, 0x950,
        0x7c0, 0x6c9, 0x5c3, 0x4ca, 0x3c6, 0x2cf, 0x1c5, 0x0cc, 0xfcc, 0xec5, 0xdcf, 0xcc6, 0xbca, 0xac3, 0x9c9, 0x8c0,
        0x8c0, 0x9c9, 0xac3, 0xbca, 0xcc6, 0xdcf, 0xec5, 0xfcc, 0x0cc, 0x1c5, 0x2cf, 0x3c6, 0x4ca, 0x5c3, 0x6c9, 0x7c0,
        0x950, 0x859, 0xb53, 0xa5a, 0xd56, 0xc5f, 0xf55, 0xe5c, 0x15c, 0x055, 0x35f, 0x256, 0x55a, 0x453, 0x759, 0x650,
        0xaf0, 0xbf9, 0x8f3, 0x9fa, 0xef6, 0xfff, 0xcf5, 0xdfc, 0x2fc, 0x3f5, 0x0ff, 0x1f6, 0x6fa, 0x7f3, 0x4f9, 0x5f0,
        0xb60, 0xa69, 0x963, 0x86a, 0xf66, 0xe6f, 0xd65, 0xc6c, 0x36c, 0x265, 0x16f, 0x066, 0x76a, 0x663, 0x569, 0x460,
        0xca0, 0xda9, 0xea3, 0xfaa, 0x8a6, 0x9af, 0xaa5, 0xbac, 0x4ac, 0x5a5, 0x6af, 0x7a6, 0x0aa, 0x1a3, 0x2a9, 0x3a0,
        0xd30, 0xc39, 0xf33, 0xe3a, 0x936, 0x83f, 0xb35, 0xa3c, 0x53c, 0x435, 0x73f, 0x636, 0x13a, 0x033, 0x339, 0x230,
        0xe90, 0xf99, 0xc93, 0xd9a, 0xa96, 0xb9f, 0x895, 0x99c, 0x69c, 0x795, 0x49f, 0x596, 0x29a, 0x393, 0x099, 0x190,
        0xf00, 0xe09, 0xd03, 0xc0a, 0xb06, 0xa0f, 0x905, 0x80c, 0x70c, 0x605, 0x50f, 0x406, 0x30a, 0x203, 0x109, 0x000
    };

    private static readonly int[,] TriangleConnectionTable =
    {
        {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 8, 3, 9, 8, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 1, 2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 2, 10, 0, 2, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 8, 3, 2, 10, 8, 10, 9, 8, -1, -1, -1, -1, -1, -1, -1},
        {3, 11, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 11, 2, 8, 11, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 11, 2, 1, 9, 11, 9, 8, 11, -1, -1, -1, -1, -1, -1, -1},
        {3, 10, 1, 11, 10, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 10, 1, 0, 8, 10, 8, 11, 10, -1, -1, -1, -1, -1, -1, -1},
        {3, 9, 0, 3, 11, 9, 11, 10, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 3, 0, 7, 3, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 1, 9, 4, 7, 1, 7, 3, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 4, 7, 3, 0, 4, 1, 2, 10, -1, -1, -1, -1, -1, -1, -1},
        {9, 2, 10, 9, 0, 2, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1},
        {2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4, -1, -1, -1, -1},
        {8, 4, 7, 3, 11, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 4, 7, 11, 2, 4, 2, 0, 4, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 1, 8, 4, 7, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1},
        {4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1, -1, -1, -1, -1},
        {3, 10, 1, 3, 11, 10, 7, 8, 4, -1, -1, -1, -1, -1, -1, -1},
        {1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4, -1, -1, -1, -1},
        {4, 7, 8, 9, 0, 11, 9, 11, 10, 11, 0, 3, -1, -1, -1, -1},
        {4, 7, 11, 4, 11, 9, 9, 11, 10, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, 0, 8, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 5, 4, 1, 5, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 5, 4, 8, 3, 5, 3, 1, 5, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 9, 5, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 1, 2, 10, 4, 9, 5, -1, -1, -1, -1, -1, -1, -1},
        {5, 2, 10, 5, 4, 2, 4, 0, 2, -1, -1, -1, -1, -1, -1, -1},
        {2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8, -1, -1, -1, -1},
        {9, 5, 4, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 11, 2, 0, 8, 11, 4, 9, 5, -1, -1, -1, -1, -1, -1, -1},
        {0, 5, 4, 0, 1, 5, 2, 3, 11, -1, -1, -1, -1, -1, -1, -1},
        {2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5, -1, -1, -1, -1},
        {10, 3, 11, 10, 1, 3, 9, 5, 4, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 5, 0, 8, 1, 8, 10, 1, 8, 11, 10, -1, -1, -1, -1},
        {5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3, -1, -1, -1, -1},
        {5, 4, 8, 5, 8, 10, 10, 8, 11, -1, -1, -1, -1, -1, -1, -1},
        {9, 7, 8, 5, 7, 9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 3, 0, 9, 5, 3, 5, 7, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 7, 8, 0, 1, 7, 1, 5, 7, -1, -1, -1, -1, -1, -1, -1},
        {1, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 7, 8, 9, 5, 7, 10, 1, 2, -1, -1, -1, -1, -1, -1, -1},
        {10, 1, 2, 9, 5, 0, 5, 3, 0, 5, 7, 3, -1, -1, -1, -1},
        {8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2, -1, -1, -1, -1},
        {2, 10, 5, 2, 5, 3, 3, 5, 7, -1, -1, -1, -1, -1, -1, -1},
        {7, 9, 5, 7, 8, 9, 3, 11, 2, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11, -1, -1, -1, -1},
        {2, 3, 11, 0, 1, 8, 1, 7, 8, 1, 5, 7, -1, -1, -1, -1},
        {11, 2, 1, 11, 1, 7, 7, 1, 5, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 8, 8, 5, 7, 10, 1, 3, 10, 3, 11, -1, -1, -1, -1},
        {5, 7, 0, 5, 0, 9, 7, 11, 0, 1, 0, 10, 11, 10, 0, -1},
        {11, 10, 0, 11, 0, 3, 10, 5, 0, 8, 0, 7, 5, 7, 0, -1},
        {11, 10, 5, 7, 11, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {10, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 1, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 8, 3, 1, 9, 8, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 5, 2, 6, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 5, 1, 2, 6, 3, 0, 8, -1, -1, -1, -1, -1, -1, -1},
        {9, 6, 5, 9, 0, 6, 0, 2, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8, -1, -1, -1, -1},
        {2, 3, 11, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 0, 8, 11, 2, 0, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 2, 3, 11, 5, 10, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 1, 9, 2, 9, 11, 2, 9, 8, 11, -1, -1, -1, -1},
        {6, 3, 11, 6, 5, 3, 5, 1, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6, -1, -1, -1, -1},
        {3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9, -1, -1, -1, -1},
        {6, 5, 9, 6, 9, 11, 11, 9, 8, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 4, 7, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 3, 0, 4, 7, 3, 6, 5, 10, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 5, 10, 6, 8, 4, 7, -1, -1, -1, -1, -1, -1, -1},
        {10, 6, 5, 1, 9, 7, 1, 7, 3, 7, 9, 4, -1, -1, -1, -1},
        {6, 1, 2, 6, 5, 1, 4, 7, 8, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 5, 5, 2, 6, 3, 0, 4, 3, 4, 7, -1, -1, -1, -1},
        {8, 4, 7, 9, 0, 5, 0, 6, 5, 0, 2, 6, -1, -1, -1, -1},
        {7, 3, 9, 7, 9, 4, 3, 2, 9, 5, 9, 6, 2, 6, 9, -1},
        {3, 11, 2, 7, 8, 4, 10, 6, 5, -1, -1, -1, -1, -1, -1, -1},
        {5, 10, 6, 4, 7, 2, 4, 2, 0, 2, 7, 11, -1, -1, -1, -1},
        {0, 1, 9, 4, 7, 8, 2, 3, 11, 5, 10, 6, -1, -1, -1, -1},
        {9, 2, 1, 9, 11, 2, 9, 4, 11, 7, 11, 4, 5, 10, 6, -1},
        {8, 4, 7, 3, 11, 5, 3, 5, 1, 5, 11, 6, -1, -1, -1, -1},
        {5, 1, 11, 5, 11, 6, 1, 0, 11, 7, 11, 4, 0, 4, 11, -1},
        {0, 5, 9, 0, 6, 5, 0, 3, 6, 11, 6, 3, 8, 4, 7, -1},
        {6, 5, 9, 6, 9, 11, 4, 7, 9, 7, 11, 9, -1, -1, -1, -1},
        {10, 4, 9, 6, 4, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 10, 6, 4, 9, 10, 0, 8, 3, -1, -1, -1, -1, -1, -1, -1},
        {10, 0, 1, 10, 6, 0, 6, 4, 0, -1, -1, -1, -1, -1, -1, -1},
        {8, 3, 1, 8, 1, 6, 8, 6, 4, 6, 1, 10, -1, -1, -1, -1},
        {1, 4, 9, 1, 2, 4, 2, 6, 4, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 1, 2, 9, 2, 4, 9, 2, 6, 4, -1, -1, -1, -1},
        {0, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 3, 2, 8, 2, 4, 4, 2, 6, -1, -1, -1, -1, -1, -1, -1},
        {10, 4, 9, 10, 6, 4, 11, 2, 3, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 2, 2, 8, 11, 4, 9, 10, 4, 10, 6, -1, -1, -1, -1},
        {3, 11, 2, 0, 1, 6, 0, 6, 4, 6, 1, 10, -1, -1, -1, -1},
        {6, 4, 1, 6, 1, 10, 4, 8, 1, 2, 1, 11, 8, 11, 1, -1},
        {9, 6, 4, 9, 3, 6, 9, 1, 3, 11, 6, 3, -1, -1, -1, -1},
        {8, 11, 1, 8, 1, 0, 11, 6, 1, 9, 1, 4, 6, 4, 1, -1},
        {3, 11, 6, 3, 6, 0, 0, 6, 4, -1, -1, -1, -1, -1, -1, -1},
        {6, 4, 8, 11, 6, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 10, 6, 7, 8, 10, 8, 9, 10, -1, -1, -1, -1, -1, -1, -1},
        {0, 7, 3, 0, 10, 7, 0, 9, 10, 6, 7, 10, -1, -1, -1, -1},
        {10, 6, 7, 1, 10, 7, 1, 7, 8, 1, 8, 0, -1, -1, -1, -1},
        {10, 6, 7, 10, 7, 1, 1, 7, 3, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 6, 1, 6, 8, 1, 8, 9, 8, 6, 7, -1, -1, -1, -1},
        {2, 6, 9, 2, 9, 1, 6, 7, 9, 0, 9, 3, 7, 3, 9, -1},
        {7, 8, 0, 7, 0, 6, 6, 0, 2, -1, -1, -1, -1, -1, -1, -1},
        {7, 3, 2, 6, 7, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 11, 10, 6, 8, 10, 8, 9, 8, 6, 7, -1, -1, -1, -1},
        {2, 0, 7, 2, 7, 11, 0, 9, 7, 6, 7, 10, 9, 10, 7, -1},
        {1, 8, 0, 1, 7, 8, 1, 10, 7, 6, 7, 10, 2, 3, 11, -1},
        {11, 2, 1, 11, 1, 7, 10, 6, 1, 6, 7, 1, -1, -1, -1, -1},
        {8, 9, 6, 8, 6, 7, 9, 1, 6, 11, 6, 3, 1, 3, 6, -1},
        {0, 9, 1, 11, 6, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 8, 0, 7, 0, 6, 3, 11, 0, 11, 6, 0, -1, -1, -1, -1},
        {7, 11, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 8, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 9, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 1, 9, 8, 3, 1, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1},
        {10, 1, 2, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 3, 0, 8, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1},
        {2, 9, 0, 2, 10, 9, 6, 11, 7, -1, -1, -1, -1, -1, -1, -1},
        {6, 11, 7, 2, 10, 3, 10, 8, 3, 10, 9, 8, -1, -1, -1, -1},
        {7, 2, 3, 6, 2, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {7, 0, 8, 7, 6, 0, 6, 2, 0, -1, -1, -1, -1, -1, -1, -1},
        {2, 7, 6, 2, 3, 7, 0, 1, 9, -1, -1, -1, -1, -1, -1, -1},
        {1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6, -1, -1, -1, -1},
        {10, 7, 6, 10, 1, 7, 1, 3, 7, -1, -1, -1, -1, -1, -1, -1},
        {10, 7, 6, 1, 7, 10, 1, 8, 7, 1, 0, 8, -1, -1, -1, -1},
        {0, 3, 7, 0, 7, 10, 0, 10, 9, 6, 10, 7, -1, -1, -1, -1},
        {7, 6, 10, 7, 10, 8, 8, 10, 9, -1, -1, -1, -1, -1, -1, -1},
        {6, 8, 4, 11, 8, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 6, 11, 3, 0, 6, 0, 4, 6, -1, -1, -1, -1, -1, -1, -1},
        {8, 6, 11, 8, 4, 6, 9, 0, 1, -1, -1, -1, -1, -1, -1, -1},
        {9, 4, 6, 9, 6, 3, 9, 3, 1, 11, 3, 6, -1, -1, -1, -1},
        {6, 8, 4, 6, 11, 8, 2, 10, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 3, 0, 11, 0, 6, 11, 0, 4, 6, -1, -1, -1, -1},
        {4, 11, 8, 4, 6, 11, 0, 2, 9, 2, 10, 9, -1, -1, -1, -1},
        {10, 9, 3, 10, 3, 2, 9, 4, 3, 11, 3, 6, 4, 6, 3, -1},
        {8, 2, 3, 8, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1},
        {0, 4, 2, 4, 6, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 9, 0, 2, 3, 4, 2, 4, 6, 4, 3, 8, -1, -1, -1, -1},
        {1, 9, 4, 1, 4, 2, 2, 4, 6, -1, -1, -1, -1, -1, -1, -1},
        {8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 10, 1, -1, -1, -1, -1},
        {10, 1, 0, 10, 0, 6, 6, 0, 4, -1, -1, -1, -1, -1, -1, -1},
        {4, 6, 3, 4, 3, 8, 6, 10, 3, 0, 3, 9, 10, 9, 3, -1},
        {10, 9, 4, 6, 10, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 5, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 4, 9, 5, 11, 7, 6, -1, -1, -1, -1, -1, -1, -1},
        {5, 0, 1, 5, 4, 0, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1},
        {11, 7, 6, 8, 3, 4, 3, 5, 4, 3, 1, 5, -1, -1, -1, -1},
        {9, 5, 4, 10, 1, 2, 7, 6, 11, -1, -1, -1, -1, -1, -1, -1},
        {6, 11, 7, 1, 2, 10, 0, 8, 3, 4, 9, 5, -1, -1, -1, -1},
        {7, 6, 11, 5, 4, 10, 4, 2, 10, 4, 0, 2, -1, -1, -1, -1},
        {3, 4, 8, 3, 5, 4, 3, 2, 5, 10, 5, 2, 11, 7, 6, -1},
        {7, 2, 3, 7, 6, 2, 5, 4, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 5, 4, 0, 8, 6, 0, 6, 2, 6, 8, 7, -1, -1, -1, -1},
        {3, 6, 2, 3, 7, 6, 1, 5, 0, 5, 4, 0, -1, -1, -1, -1},
        {6, 2, 8, 6, 8, 7, 2, 1, 8, 4, 8, 5, 1, 5, 8, -1},
        {9, 5, 4, 10, 1, 6, 1, 7, 6, 1, 3, 7, -1, -1, -1, -1},
        {1, 6, 10, 1, 7, 6, 1, 0, 7, 8, 7, 0, 9, 5, 4, -1},
        {4, 0, 10, 4, 10, 5, 0, 3, 10, 6, 10, 7, 3, 7, 10, -1},
        {7, 6, 10, 7, 10, 8, 5, 4, 10, 4, 8, 10, -1, -1, -1, -1},
        {6, 9, 5, 6, 11, 9, 11, 8, 9, -1, -1, -1, -1, -1, -1, -1},
        {3, 6, 11, 0, 6, 3, 0, 5, 6, 0, 9, 5, -1, -1, -1, -1},
        {0, 11, 8, 0, 5, 11, 0, 1, 5, 5, 6, 11, -1, -1, -1, -1},
        {6, 11, 3, 6, 3, 5, 5, 3, 1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 10, 9, 5, 11, 9, 11, 8, 11, 5, 6, -1, -1, -1, -1},
        {0, 11, 3, 0, 6, 11, 0, 9, 6, 5, 6, 9, 1, 2, 10, -1},
        {11, 8, 5, 11, 5, 6, 8, 0, 5, 10, 5, 2, 0, 2, 5, -1},
        {6, 11, 3, 6, 3, 5, 2, 10, 3, 10, 5, 3, -1, -1, -1, -1},
        {5, 8, 9, 5, 2, 8, 5, 6, 2, 3, 8, 2, -1, -1, -1, -1},
        {9, 5, 6, 9, 6, 0, 0, 6, 2, -1, -1, -1, -1, -1, -1, -1},
        {1, 5, 8, 1, 8, 0, 5, 6, 8, 3, 8, 2, 6, 2, 8, -1},
        {1, 5, 6, 2, 1, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 3, 6, 1, 6, 10, 3, 8, 6, 5, 6, 9, 8, 9, 6, -1},
        {10, 1, 0, 10, 0, 6, 9, 5, 0, 5, 6, 0, -1, -1, -1, -1},
        {0, 3, 8, 5, 6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {10, 5, 6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 5, 10, 7, 5, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {11, 5, 10, 11, 7, 5, 8, 3, 0, -1, -1, -1, -1, -1, -1, -1},
        {5, 11, 7, 5, 10, 11, 1, 9, 0, -1, -1, -1, -1, -1, -1, -1},
        {10, 7, 5, 10, 11, 7, 9, 8, 1, 8, 3, 1, -1, -1, -1, -1},
        {11, 1, 2, 11, 7, 1, 7, 5, 1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 1, 2, 7, 1, 7, 5, 7, 2, 11, -1, -1, -1, -1},
        {9, 7, 5, 9, 2, 7, 9, 0, 2, 2, 11, 7, -1, -1, -1, -1},
        {7, 5, 2, 7, 2, 11, 5, 9, 2, 3, 2, 8, 9, 8, 2, -1},
        {2, 5, 10, 2, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1},
        {8, 2, 0, 8, 5, 2, 8, 7, 5, 10, 2, 5, -1, -1, -1, -1},
        {9, 0, 1, 5, 10, 3, 5, 3, 7, 3, 10, 2, -1, -1, -1, -1},
        {9, 8, 2, 9, 2, 1, 8, 7, 2, 10, 2, 5, 7, 5, 2, -1},
        {1, 3, 5, 3, 7, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 7, 0, 7, 1, 1, 7, 5, -1, -1, -1, -1, -1, -1, -1},
        {9, 0, 3, 9, 3, 5, 5, 3, 7, -1, -1, -1, -1, -1, -1, -1},
        {9, 8, 7, 5, 9, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {5, 8, 4, 5, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1},
        {5, 0, 4, 5, 11, 0, 5, 10, 11, 11, 3, 0, -1, -1, -1, -1},
        {0, 1, 9, 8, 4, 10, 8, 10, 11, 10, 4, 5, -1, -1, -1, -1},
        {10, 11, 4, 10, 4, 5, 11, 3, 4, 9, 4, 1, 3, 1, 4, -1},
        {2, 5, 1, 2, 8, 5, 2, 11, 8, 4, 5, 8, -1, -1, -1, -1},
        {0, 4, 11, 0, 11, 3, 4, 5, 11, 2, 11, 1, 5, 1, 11, -1},
        {0, 2, 5, 0, 5, 9, 2, 11, 5, 4, 5, 8, 11, 8, 5, -1},
        {9, 4, 5, 2, 11, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 5, 10, 3, 5, 2, 3, 4, 5, 3, 8, 4, -1, -1, -1, -1},
        {5, 10, 2, 5, 2, 4, 4, 2, 0, -1, -1, -1, -1, -1, -1, -1},
        {3, 10, 2, 3, 5, 10, 3, 8, 5, 4, 5, 8, 0, 1, 9, -1},
        {5, 10, 2, 5, 2, 4, 1, 9, 2, 9, 4, 2, -1, -1, -1, -1},
        {8, 4, 5, 8, 5, 3, 3, 5, 1, -1, -1, -1, -1, -1, -1, -1},
        {0, 4, 5, 1, 0, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {8, 4, 5, 8, 5, 3, 9, 0, 5, 0, 3, 5, -1, -1, -1, -1},
        {9, 4, 5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 11, 7, 4, 9, 11, 9, 10, 11, -1, -1, -1, -1, -1, -1, -1},
        {0, 8, 3, 4, 9, 7, 9, 11, 7, 9, 10, 11, -1, -1, -1, -1},
        {1, 10, 11, 1, 11, 4, 1, 4, 0, 7, 4, 11, -1, -1, -1, -1},
        {3, 1, 4, 3, 4, 8, 1, 10, 4, 7, 4, 11, 10, 11, 4, -1},
        {4, 11, 7, 9, 11, 4, 9, 2, 11, 9, 1, 2, -1, -1, -1, -1},
        {9, 7, 4, 9, 11, 7, 9, 1, 11, 2, 11, 1, 0, 8, 3, -1},
        {11, 7, 4, 11, 4, 2, 2, 4, 0, -1, -1, -1, -1, -1, -1, -1},
        {11, 7, 4, 11, 4, 2, 8, 3, 4, 3, 2, 4, -1, -1, -1, -1},
        {2, 9, 10, 2, 7, 9, 2, 3, 7, 7, 4, 9, -1, -1, -1, -1},
        {9, 10, 7, 9, 7, 4, 10, 2, 7, 8, 7, 0, 2, 0, 7, -1},
        {3, 7, 10, 3, 10, 2, 7, 4, 10, 1, 10, 0, 4, 0, 10, -1},
        {1, 10, 2, 8, 7, 4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 1, 4, 1, 7, 7, 1, 3, -1, -1, -1, -1, -1, -1, -1},
        {4, 9, 1, 4, 1, 7, 0, 8, 1, 8, 7, 1, -1, -1, -1, -1},
        {4, 0, 3, 7, 4, 3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {4, 8, 7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {9, 10, 8, 10, 11, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 9, 3, 9, 11, 11, 9, 10, -1, -1, -1, -1, -1, -1, -1},
        {0, 1, 10, 0, 10, 8, 8, 10, 11, -1, -1, -1, -1, -1, -1, -1},
        {3, 1, 10, 11, 3, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 2, 11, 1, 11, 9, 9, 11, 8, -1, -1, -1, -1, -1, -1, -1},
        {3, 0, 9, 3, 9, 11, 1, 2, 9, 2, 11, 9, -1, -1, -1, -1},
        {0, 2, 11, 8, 0, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {3, 2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 8, 2, 8, 10, 10, 8, 9, -1, -1, -1, -1, -1, -1, -1},
        {9, 10, 2, 0, 9, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {2, 3, 8, 2, 8, 10, 0, 1, 8, 1, 10, 8, -1, -1, -1, -1},
        {1, 10, 2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {1, 3, 8, 9, 1, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 9, 1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {0, 3, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1},
        {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1}
    };

    #endregion

    #region Monobehaviour Methods

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        EnsureMesh();
        EnsureGrid();
        HideScanlinePreviews();
        inputActions = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        if (inputActions == null)
        {
            inputActions = new InputSystem_Actions();
        }

        inputActions.Enable();
        inputActions.Player.Toggle.started += OnToggleShape;
        inputActions.Player.Morph.performed += OnScanModePerformed;
        inputActions.Player.Morph.canceled += OnScanModeCanceled;
    }

    private void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Toggle.started -= OnToggleShape;
        inputActions.Player.Morph.performed -= OnScanModePerformed;
        inputActions.Player.Morph.canceled -= OnScanModeCanceled;
        inputActions.Disable();
    }

    private void Update()
    {
        HandleScanlineInput();
        UpdateScanlinePreviewLive();
        UpdateScanlineHoldBudget();
    }

    private void Start()
    {
        if (!generateOnStart)
        {
            return;
        }

        if (fillSolidOnStart)
        {
            FillSphere();
        }

        RebuildMesh();
    }
    #endregion

    private void HandleScanlineInput()
    {
        if (!(isRightMouseHeld || scanlineRoutine != null) || inputActions == null)
        {
            return;
        }

        Vector2 lookDelta = inputActions.Player.Look.ReadValue<Vector2>();
        if (lookDelta != Vector2.zero)
        {
            scanlineShapeScaleX = Mathf.Clamp01(scanlineShapeScaleX + lookDelta.x * scanlineMouseScaleSensitivity);
            scanlineShapeScaleZ = Mathf.Clamp01(scanlineShapeScaleZ + lookDelta.y * scanlineMouseScaleSensitivity);
        }

        Vector2 scroll = inputActions.UI.ScrollWheel.ReadValue<Vector2>();
        if (scroll.y != 0f)
        {
            float delta = scroll.y * scanlineScrollScaleSensitivity;
            scanlineShapeScaleX = Mathf.Clamp01(scanlineShapeScaleX + delta);
            scanlineShapeScaleZ = Mathf.Clamp01(scanlineShapeScaleZ + delta);
        }
    }

    private void UpdateScanlinePreviewLive()
    {
        bool showPreview = scanlineRoutine != null || isRightMouseHeld;
        if (!showPreview)
        {
            HideScanlinePreviews();
            return;
        }

        int layer = scanlineRoutine != null && scanlineCurrentLayer >= 0 ? scanlineCurrentLayer : 0;
        Vector2 halfExtents = GetScanlineHalfExtents();
        Vector2 center = new Vector2((size.x - 1) * 0.5f, (size.z - 1) * 0.5f);
        UpdateScanlinePreview(layer, center, halfExtents);
    }

    private void UpdateScanlineHoldBudget()
    {
        if (!isRightMouseHeld || scanlineRoutine != null || !scanlinePendingStart)
        {
            return;
        }

        EnsureScanlineBudgetInitialized();
        if (pauseBudgetRemaining <= 0f)
        {
            StartScanline();
            return;
        }

        pauseBudgetRemaining = Mathf.Max(0f, pauseBudgetRemaining - Time.deltaTime);
        if (pauseBudgetRemaining <= 0f)
        {
            StartScanline();
        }
    }


    private void OnValidate()
    {
        size = new Vector3Int(
            Mathf.Clamp(size.x, 1, MaxSize),
            Mathf.Clamp(size.y, 1, MaxSize),
            Mathf.Clamp(size.z, 1, MaxSize)
        );
        voxelSize = Mathf.Max(0.01f, voxelSize);
        isoLevel = Mathf.Clamp01(isoLevel);
        scanlineLayersPerSecond = Mathf.Max(0.01f, scanlineLayersPerSecond);
        scanlineShapeScaleX = Mathf.Clamp01(scanlineShapeScaleX);
        scanlineShapeScaleZ = Mathf.Clamp01(scanlineShapeScaleZ);
        scanlineMouseScaleSensitivity = Mathf.Max(0f, scanlineMouseScaleSensitivity);
        scanlineScrollScaleSensitivity = Mathf.Max(0f, scanlineScrollScaleSensitivity);
        scanlinePauseBudgetSeconds = Mathf.Max(0f, scanlinePauseBudgetSeconds);
        scanDistanceMax = Mathf.Max(0.01f, scanDistanceMax);

        bool sizeChanged = size != lastValidatedSize;
        bool meshAffectingChanged =
            size != lastValidatedSize ||
            !Mathf.Approximately(voxelSize, lastValidatedVoxelSize) ||
            !Mathf.Approximately(isoLevel, lastValidatedIsoLevel);

        lastValidatedSize = size;
        lastValidatedVoxelSize = voxelSize;
        lastValidatedIsoLevel = isoLevel;

        if (!Application.isPlaying)
        {
            return;
        }

        if (!meshAffectingChanged)
        {
            return;
        }

        EnsureGrid();
        if (sizeChanged && regenerateOnValidate && generateOnStart)
        {
            if (fillSolidOnStart)
            {
                FillSphere();
            }
            else
            {
                Clear();
            }
        }
        RebuildMesh();
    }

    public void Resize(Vector3Int newSize, bool clear = true)
    {
        size = new Vector3Int(
            Mathf.Clamp(newSize.x, 1, MaxSize),
            Mathf.Clamp(newSize.y, 1, MaxSize),
            Mathf.Clamp(newSize.z, 1, MaxSize)
        );
        voxels = new float[size.x, size.y, size.z];
        colors = new Color[size.x, size.y, size.z];
        if (!clear)
        {
            return;
        }

        Clear();
        RebuildMesh();
    }

    public void Clear()
    {
        if (voxels == null)
        {
            return;
        }

        System.Array.Clear(voxels, 0, voxels.Length);
        if (colors == null)
        {
            return;
        }

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    colors[x, y, z] = Color.black;
                }
            }
        }
    }

    public void FillSolid(float value = 1f)
    {
        EnsureGrid();
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    voxels[x, y, z] = value;
                    colors[x, y, z] = fillColor;
                }
            }
        }
    }

    public void FillSphere(float value = 1f)
    {
        EnsureGrid();

        Vector3 center = new Vector3(size.x - 1, size.y - 1, size.z - 1) * 0.5f;
        float radius = Mathf.Min(size.x - 1, Mathf.Min(size.y - 1, size.z - 1)) * 0.5f;
        float safeRadius = Mathf.Max(0.0001f, radius);
        float radiusSq = radius * radius;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    Vector3 p = new Vector3(x, y, z);
                    Vector3 delta = p - center;
                    if (delta.sqrMagnitude <= radiusSq)
                    {
                        float dist = delta.magnitude;
                        float density = Mathf.Clamp01(1f - (dist / safeRadius));
                        voxels[x, y, z] = density * value;
                        colors[x, y, z] = fillColor;
                    }
                    else
                    {
                        voxels[x, y, z] = 0f;
                        colors[x, y, z] = Color.black;
                    }
                }
            }
        }
    }

    public void StartScanline()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (scanlineRoutine != null)
        {
            return;
        }

        EnsureScanlineBudgetInitialized();
        scanlinePendingStart = false;
        scanlineRoutine = StartCoroutine(ScanlineRoutine());
    }

    public void SetScanlineShape(ScanlineShape shape, Vector2 xzScale)
    {
        scanlineShape = shape;
        scanlineShapeScaleX = Mathf.Clamp01(xzScale.x);
        scanlineShapeScaleZ = Mathf.Clamp01(xzScale.y);
    }

    public void SetVoxel(int x, int y, int z, bool filled)
    {
        if (!InBounds(x, y, z))
        {
            return;
        }

        voxels[x, y, z] = filled ? 1f : 0f;
        if (colors != null)
        {
            colors[x, y, z] = filled ? fillColor : Color.black;
        }
    }

    public void SetVoxel(int x, int y, int z, float value, Color color)
    {
        if (!InBounds(x, y, z))
        {
            return;
        }

        voxels[x, y, z] = Mathf.Clamp01(value);
        colors[x, y, z] = color;
    }

    public void ScanObjectToGrid(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        EnsureScanGrid();
        MeshFilter meshFilterTarget = target.GetComponent<MeshFilter>();
        Mesh meshTarget = meshFilterTarget != null ? meshFilterTarget.sharedMesh : null;
        if (meshTarget != null && !meshTarget.isReadable)
        {
            meshTarget = null;
        }

        MeshRenderer rendererTarget = target.GetComponent<MeshRenderer>();
        Color fallbackColor = rendererTarget != null && rendererTarget.sharedMaterial != null
            ? rendererTarget.sharedMaterial.color
            : Color.white;

        Color[] vertexColors = meshTarget != null ? meshTarget.colors : null;
        Vector3[] vertices = meshTarget != null ? meshTarget.vertices : System.Array.Empty<Vector3>();
        bool hasVertexColors = vertexColors != null && vertexColors.Length == vertices.Length && vertices.Length > 0;

        Transform targetTransform = target.transform;
        Matrix4x4 targetLocalToWorld = targetTransform.localToWorldMatrix;
        Matrix4x4 targetWorldToLocal = targetTransform.worldToLocalMatrix;
        Vector3 targetPosition = targetTransform.position;
        Quaternion targetRotation = targetTransform.rotation;
        Vector3 targetScale = targetTransform.lossyScale;

        bool canUseMeshDistance = meshTarget != null && meshTarget.triangles != null && meshTarget.triangles.Length > 0;
        MeshAccel meshAccel = new MeshAccel();
        int[] triVisit = null;
        int visitMark = 1;

        Collider scanCollider = target.GetComponent<Collider>();
        bool canUseCollider = scanCollider != null;
        bool useMeshDistance = scanUseMeshDistance ? canUseMeshDistance : !canUseCollider && canUseMeshDistance;
        GameObject probeObject = null;
        SphereCollider probeCollider = null;

        if (useMeshDistance)
        {
            bool meshChanged = meshTarget != cachedScanMesh;
            bool voxelChanged = !Mathf.Approximately(cachedScanVoxelSize, voxelSize);
            bool transformChanged = targetPosition != cachedScanTargetPosition ||
                targetRotation != cachedScanTargetRotation ||
                targetScale != cachedScanTargetScale;

            if (meshChanged)
            {
                cachedScanMesh = meshTarget;
                cachedScanVertices = meshTarget.vertices;
                cachedScanTriangles = meshTarget.triangles;
            }

            if (meshChanged || voxelChanged || transformChanged)
            {
                if (cachedScanVertices == null || cachedScanTriangles == null)
                {
                    cachedScanVertices = meshTarget.vertices;
                    cachedScanTriangles = meshTarget.triangles;
                }

                cachedScanWorldVertices = TransformVertices(cachedScanVertices, targetLocalToWorld);
                cachedScanWorldBounds = ComputeBounds(cachedScanWorldVertices);
                cachedScanAccel = BuildMeshAccel(cachedScanWorldVertices, cachedScanTriangles);
                cachedScanVoxelSize = voxelSize;
                cachedScanTargetPosition = targetPosition;
                cachedScanTargetRotation = targetRotation;
                cachedScanTargetScale = targetScale;
            }

            vertices = cachedScanVertices;
            meshAccel = cachedScanAccel;
            triVisit = new int[meshAccel.tris != null ? meshAccel.tris.Length : 0];
        }
        else
        {
            if (!canUseCollider)
            {
                return;
            }

            probeObject = new GameObject("ScanProbe");
            probeObject.hideFlags = HideFlags.HideAndDontSave;
            probeCollider = probeObject.AddComponent<SphereCollider>();
            probeCollider.radius = Mathf.Max(0.0001f, voxelSize * 0.001f);
        }
        Vector3 gridCenter = GetGridCenter();
        Vector3 scanOffset = targetPosition - transform.position;
        Matrix4x4 blobToWorld = transform.localToWorldMatrix;
        Matrix4x4 blobWorldToLocal = transform.worldToLocalMatrix;
        Matrix4x4 targetLocalToWorldAligned = targetLocalToWorld;
        targetLocalToWorldAligned.SetColumn(
            3,
            new Vector4(transform.position.x, transform.position.y, transform.position.z, 1f)
        );

        if (scanUseRaycastToCenter)
        {
            if (!canUseCollider)
            {
                return;
            }

            ScanObjectToGridRaycastToCenter(
                scanCollider,
                vertices,
                vertexColors,
                hasVertexColors,
                fallbackColor,
                gridCenter,
                blobToWorld,
                targetWorldToLocal,
                scanOffset
            );
            return;
        }
        Bounds meshBounds = default;
        float maxDistanceSq = 0f;
        if (useMeshDistance)
        {
            meshBounds = cachedScanWorldBounds;
            float maxDistance = scanDistanceMax;
            maxDistanceSq = maxDistance * maxDistance;
        }

        if (useMeshDistance && scanUseSurfaceVoxelization)
        {
            ScanObjectToGridSurfaceVoxelization(
                meshTarget,
                vertices,
                vertexColors,
                hasVertexColors,
                fallbackColor,
                gridCenter,
                blobToWorld,
                blobWorldToLocal,
                targetWorldToLocal,
                targetLocalToWorldAligned,
                scanOffset
            );
            return;
        }

        if (useMeshDistance && scanUseParallel)
        {
            int triCount = meshAccel.tris != null ? meshAccel.tris.Length : 0;
            var triVisitLocal = new ThreadLocal<int[]>(() => new int[triCount]);
            var visitMarkLocal = new ThreadLocal<int>(() => 1);

            Parallel.For(0, size.x, x =>
            {
                int[] triVisitThread = triVisitLocal.Value;
                int visitMarkThread = visitMarkLocal.Value;
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        Vector3 local = new Vector3(x, y, z) - gridCenter;
                        Vector3 world = blobToWorld.MultiplyPoint3x4(local * voxelSize);
                        Vector3 scanWorld = world + scanOffset;
                        Vector3 scanLocalColor = targetWorldToLocal.MultiplyPoint3x4(scanWorld);
                        if (meshBounds.SqrDistance(scanWorld) > maxDistanceSq)
                        {
                            scanVoxels[x, y, z] = 0f;
                            scanColors[x, y, z] = fallbackColor;
                            continue;
                        }
                        float signedDistance = GetSignedDistanceToMeshAccelerated(scanWorld, meshAccel, triVisitThread, ref visitMarkThread);
                        float value = Mathf.Clamp01(isoLevel - (signedDistance / (2f * scanDistanceMax)));
                        scanVoxels[x, y, z] = value;
                        scanColors[x, y, z] = SampleScanColorLocal(scanLocalColor, vertices, vertexColors, hasVertexColors, fallbackColor);
                    }
                }

                visitMarkLocal.Value = visitMarkThread;
            });

            triVisitLocal.Dispose();
            visitMarkLocal.Dispose();
        }
        else
        {
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        Vector3 local = new Vector3(x, y, z) - gridCenter;
                        Vector3 world = blobToWorld.MultiplyPoint3x4(local * voxelSize);
                        Vector3 scanWorld = world + scanOffset;
                        float signedDistance;
                        if (useMeshDistance)
                        {
                            if (meshBounds.SqrDistance(scanWorld) > maxDistanceSq)
                            {
                                scanVoxels[x, y, z] = 0f;
                                scanColors[x, y, z] = fallbackColor;
                                continue;
                            }
                            signedDistance = GetSignedDistanceToMeshAccelerated(scanWorld, meshAccel, triVisit, ref visitMark);
                        }
                        else
                        {
                            probeObject.transform.position = scanWorld;
                            signedDistance = GetSignedDistance(scanWorld, scanCollider, probeCollider);
                        }
                        float value = Mathf.Clamp01(isoLevel - (signedDistance / (2f * scanDistanceMax)));
                        scanVoxels[x, y, z] = value;
                        Vector3 scanLocalColor = targetWorldToLocal.MultiplyPoint3x4(scanWorld);
                        scanColors[x, y, z] = SampleScanColorLocal(scanLocalColor, vertices, vertexColors, hasVertexColors, fallbackColor);
                    }
                }
            }
        }

        if (probeObject != null)
        {
            if (Application.isPlaying)
            {
                Destroy(probeObject);
            }
            else
            {
                DestroyImmediate(probeObject);
            }
        }
    }

    public void ApplyScanColorsToGrid()
    {
        EnsureGrid();
        EnsureScanGrid();
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    colors[x, y, z] = scanColors[x, y, z];
                }
            }
        }
    }

    public void ApplyScanGridToMesh()
    {
        EnsureGrid();
        EnsureScanGrid();
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    voxels[x, y, z] = scanVoxels[x, y, z];
                    colors[x, y, z] = scanColors[x, y, z];
                }
            }
        }

        RebuildMesh();
    }

    public float CompareToScanGrid(int rotationSteps, bool includeColors, out float bestRotationDegrees)
    {
        EnsureScanGrid();
        EnsureGrid();

        bestRotationDegrees = 0f;
        float score = ComputeSimilarity(0f, includeColors);
        return Mathf.Clamp01(score);
    }

    public float CompareToScanGridAdvanced(
        int rotationSteps,
        int scaleSteps,
        float scaleRange,
        int yOffsetSteps,
        int yOffsetRange,
        bool includeColors,
        out float bestRotationDegrees,
        out float bestScale,
        out int bestYOffset)
    {
        EnsureScanGrid();
        EnsureGrid();

        float bestScore = float.PositiveInfinity;
        bestRotationDegrees = 0f;
        bestScale = 1f;
        bestYOffset = 0;

        float score = ComputeSimilarity(0f, 1f, 0, includeColors);
        bestScore = score;
        return Mathf.Clamp01(bestScore);
    }

    public bool GetVoxel(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
        {
            return false;
        }

        return voxels[x, y, z] > 0f;
    }

    public void RebuildMesh()
    {
        EnsureMesh();
        EnsureGrid();

        var vertices = new List<Vector3>(2048);
        var triangles = new List<int>(4096);
        var vertexColors = new List<Color>(2048);
        var vertexColorCounts = new List<int>(2048);
        List<Vector3> vertexNormals = useGradientNormals ? new List<Vector3>(2048) : null;
        List<int> vertexNormalCounts = useGradientNormals ? new List<int>(2048) : null;
        var vertexLookup = new Dictionary<VertexKey, int>(4096);
        var cube = new float[8];
        var edgeVertex = new Vector3[12];
        var edgeColor = new Color[12];
        const float vertexKeyPrecision = 10000f;

        for (int x = -1; x < size.x; x++)
        {
            for (int y = -1; y < size.y; y++)
            {
                for (int z = -1; z < size.z; z++)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        int vx = x + VertexOffset[i, 0];
                        int vy = y + VertexOffset[i, 1];
                        int vz = z + VertexOffset[i, 2];
                        cube[i] = GetVoxelValueSafe(vx, vy, vz);
                    }

                    int flagIndex = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (cube[i] >= isoLevel)
                        {
                            flagIndex |= 1 << i;
                        }
                    }

                    int edgeFlags = CubeEdgeFlags[flagIndex];
                    if (edgeFlags == 0)
                    {
                        continue;
                    }

                    Vector3 basePos = new Vector3(x, y, z) - GetGridCenter();
                    for (int i = 0; i < 12; i++)
                    {
                        if ((edgeFlags & (1 << i)) == 0)
                        {
                            continue;
                        }

                        int v0 = EdgeConnection[i, 0];
                        int v1 = EdgeConnection[i, 1];
                        float offset = GetOffset(cube[v0], cube[v1]);
                        Vector3 p0 = basePos + new Vector3(VertexOffset[v0, 0], VertexOffset[v0, 1], VertexOffset[v0, 2]);
                        Vector3 p1 = basePos + new Vector3(VertexOffset[v1, 0], VertexOffset[v1, 1], VertexOffset[v1, 2]);
                        Vector3 pos = Vector3.Lerp(p0, p1, offset);
                        edgeVertex[i] = pos * voxelSize;
                        Vector3 colorPos = new Vector3(x, y, z) + Vector3.Lerp(
                            new Vector3(VertexOffset[v0, 0], VertexOffset[v0, 1], VertexOffset[v0, 2]),
                            new Vector3(VertexOffset[v1, 0], VertexOffset[v1, 1], VertexOffset[v1, 2]),
                            offset
                        );
                        edgeColor[i] = SampleColorByDensity(colorPos.x, colorPos.y, colorPos.z);
                    }

                    for (int i = 0; i < 16; i += 3)
                    {
                        int a = TriangleConnectionTable[flagIndex, i];
                        if (a < 0)
                        {
                            break;
                        }

                        int b = TriangleConnectionTable[flagIndex, i + 1];
                        int c = TriangleConnectionTable[flagIndex, i + 2];
        int ia = GetOrCreateVertex(edgeVertex[a], edgeColor[a], vertices, vertexColors, vertexColorCounts, vertexNormals, vertexNormalCounts, vertexLookup, vertexKeyPrecision);
        int ib = GetOrCreateVertex(edgeVertex[b], edgeColor[b], vertices, vertexColors, vertexColorCounts, vertexNormals, vertexNormalCounts, vertexLookup, vertexKeyPrecision);
        int ic = GetOrCreateVertex(edgeVertex[c], edgeColor[c], vertices, vertexColors, vertexColorCounts, vertexNormals, vertexNormalCounts, vertexLookup, vertexKeyPrecision);
                        triangles.Add(ia);
                        triangles.Add(ic);
                        triangles.Add(ib);
                    }
                }
            }
        }

        mesh.Clear();
        if (vertices.Count == 0)
        {
            meshFilter.sharedMesh = mesh;
            return;
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(vertexColors);
        if (useGradientNormals && vertexNormals != null)
        {
            Vector3[] triangleNormalSum = new Vector3[vertices.Count];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                Vector3 a = vertices[ia];
                Vector3 b = vertices[ib];
                Vector3 c = vertices[ic];
                Vector3 n = Vector3.Cross(b - a, c - a);
                triangleNormalSum[ia] += n;
                triangleNormalSum[ib] += n;
                triangleNormalSum[ic] += n;
            }

            for (int i = 0; i < vertexNormals.Count; i++)
            {
                Vector3 n = vertexNormals[i];
                if (triangleNormalSum[i].sqrMagnitude > 0.000001f &&
                    Vector3.Dot(n, triangleNormalSum[i]) < 0f)
                {
                    n = -n;
                }
                vertexNormals[i] = n.sqrMagnitude > 0.000001f ? n.normalized : Vector3.up;
            }
            mesh.SetNormals(vertexNormals);
        }
        else
        {
            mesh.RecalculateNormals();
        }
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;
    }

    private int GetOrCreateVertex(
        Vector3 position,
        Color color,
        List<Vector3> vertices,
        List<Color> colors,
        List<int> colorCounts,
        List<Vector3> normals,
        List<int> normalCounts,
        Dictionary<VertexKey, int> lookup,
        float precision)
    {
        VertexKey key = new VertexKey(position, precision);
        if (lookup.TryGetValue(key, out int index))
        {
            int count = colorCounts[index];
            colors[index] = (colors[index] * count + color) / (count + 1);
            colorCounts[index] = count + 1;

            if (normals != null && normalCounts != null)
            {
                int ncount = normalCounts[index];
                Vector3 normal = ComputeNormal(position);
                normals[index] = (normals[index] * ncount + normal) / (ncount + 1);
                normalCounts[index] = ncount + 1;
            }
            return index;
        }

        int newIndex = vertices.Count;
        vertices.Add(position);
        colors.Add(color);
        colorCounts.Add(1);
        if (normals != null && normalCounts != null)
        {
            normals.Add(ComputeNormal(position));
            normalCounts.Add(1);
        }
        lookup.Add(key, newIndex);
        return newIndex;
    }

    private void EnsureGrid()
    {
        if (voxels != null &&
            colors != null &&
            voxels.GetLength(0) == size.x &&
            voxels.GetLength(1) == size.y &&
            voxels.GetLength(2) == size.z &&
            colors.GetLength(0) == size.x &&
            colors.GetLength(1) == size.y &&
            colors.GetLength(2) == size.z)
        {
            return;
        }

        voxels = new float[size.x, size.y, size.z];
        colors = new Color[size.x, size.y, size.z];
    }

    private void EnsureScanGrid()
    {
        if (scanVoxels != null &&
            scanColors != null &&
            scanVoxels.GetLength(0) == size.x &&
            scanVoxels.GetLength(1) == size.y &&
            scanVoxels.GetLength(2) == size.z)
        {
            return;
        }

        scanVoxels = new float[size.x, size.y, size.z];
        scanColors = new Color[size.x, size.y, size.z];
    }

    private IEnumerator ScanlineRoutine()
    {
        EnsureGrid();
        EnsureScanlineBudgetInitialized();
        float delay = 1f / Mathf.Max(0.01f, scanlineLayersPerSecond);

        for (int y = 0; y < size.y; y++)
        {
            Color layerColor = fillColor;
            Vector2 halfExtents = GetScanlineHalfExtents();
            Vector2 center = new Vector2((size.x - 1) * 0.5f, (size.z - 1) * 0.5f);
            scanlineCurrentLayer = y;
            UpdateScanlinePreview(y, center, halfExtents);
            bool zeroScale = scanlineShapeScaleX <= 0f || scanlineShapeScaleZ <= 0f;
            for (int x = 0; x < size.x; x++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    colors[x, y, z] = layerColor;
                    voxels[x, y, z] = zeroScale ? 0f : GetScanlineVoxelValue(x, z, center, halfExtents);
                }
            }

            RebuildMesh();
            yield return WaitForScanlineDelay(delay);
        }

        scanlineRoutine = null;
        scanlineCurrentLayer = -1;
        scanlinePendingStart = false;
        scanlineBudgetInitialized = false;
        pauseBudgetRemaining = 0f;
        HideScanlinePreviews();
    }

    private IEnumerator WaitForScanlineDelay(float delay)
    {
        float remaining = delay;
        while (remaining > 0f)
        {
            if (isRightMouseHeld && pauseBudgetRemaining > 0f)
            {
                float dt = Time.deltaTime;
                pauseBudgetRemaining = Mathf.Max(0f, pauseBudgetRemaining - dt);
                yield return null;
                continue;
            }

            remaining -= Time.deltaTime;
            yield return null;
        }
    }

    private Vector2 GetScanlineHalfExtents()
    {
        float halfX = Mathf.Max(0.5f, (size.x - 1) * 0.5f * scanlineShapeScaleX);
        float halfZ = Mathf.Max(0.5f, (size.z - 1) * 0.5f * scanlineShapeScaleZ);
        return new Vector2(halfX, halfZ);
    }

    private void UpdateScanlinePreview(int y, Vector2 center, Vector2 halfExtents)
    {
        Transform cube = scanlineCubePreview;
        Transform cylinder = scanlineCylinderPreview;

        if (cube != null)
        {
            cube.gameObject.SetActive(scanlineShape == ScanlineShape.Cube);
        }

        if (cylinder != null)
        {
            cylinder.gameObject.SetActive(scanlineShape == ScanlineShape.Cylinder);
        }

        Transform active = scanlineShape == ScanlineShape.Cube ? cube : cylinder;
        if (active == null)
        {
            return;
        }

        Vector3 localPosition = new Vector3(center.x, y, center.y) - GetGridCenter();
        localPosition *= voxelSize;
        active.position = transform.TransformPoint(localPosition);
        active.rotation = transform.rotation * Quaternion.Euler(0f, scanlineShapeRotationDegrees, 0f);
        float height = voxelSize * 0.1f;
        Vector2 previewHalfExtents = halfExtents;
        if (scanlineShape == ScanlineShape.Cylinder)
        {
            // The cylinder uses a 1 - distance falloff; the iso surface is at (1 - isoLevel).
            previewHalfExtents *= Mathf.Max(0f, 1f - isoLevel);
        }
        active.localScale = new Vector3(
            previewHalfExtents.x * 2f * voxelSize,
            height,
            previewHalfExtents.y * 2f * voxelSize
        );
    }

    private void HideScanlinePreviews()
    {
        if (scanlineCubePreview != null)
        {
            scanlineCubePreview.gameObject.SetActive(false);
        }

        if (scanlineCylinderPreview != null)
        {
            scanlineCylinderPreview.gameObject.SetActive(false);
        }
    }

    private void OnToggleShape(InputAction.CallbackContext context)
    {
        scanlineShape = scanlineShape == ScanlineShape.Cube ? ScanlineShape.Cylinder : ScanlineShape.Cube;
    }

    private void OnScanModePerformed(InputAction.CallbackContext context)
    {
        isRightMouseHeld = true;
        if (scanlineRoutine == null)
        {
            scanlinePendingStart = true;
            EnsureScanlineBudgetInitialized();
        }
    }

    private void OnScanModeCanceled(InputAction.CallbackContext context)
    {
        isRightMouseHeld = false;
        if (scanlineRoutine == null && scanlinePendingStart)
        {
            StartScanline();
            scanlinePendingStart = false;
        }
    }

    private void EnsureScanlineBudgetInitialized()
    {
        if (scanlineBudgetInitialized)
        {
            return;
        }

        pauseBudgetRemaining = scanlinePauseBudgetSeconds;
        scanlineBudgetInitialized = true;
    }

    private float GetScanlineVoxelValue(int x, int z, Vector2 center, Vector2 halfExtents)
    {
        float dx = x - center.x;
        float dz = z - center.y;
        if (Mathf.Abs(scanlineShapeRotationDegrees) > 0.001f)
        {
            float radians = scanlineShapeRotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            float rx = dx * cos - dz * sin;
            float rz = dx * sin + dz * cos;
            dx = rx;
            dz = rz;
        }

        if (scanlineShape == ScanlineShape.Cube)
        {
            return Mathf.Abs(dx) <= halfExtents.x && Mathf.Abs(dz) <= halfExtents.y ? 1f : 0f;
        }

        float nx = dx / halfExtents.x;
        float nz = dz / halfExtents.y;
        float distance = Mathf.Sqrt(nx * nx + nz * nz);
        return Mathf.Clamp01(1f - distance);
    }

    private void EnsureMesh()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                return;
            }
        }

        if (mesh != null)
        {
            return;
        }

        mesh = new Mesh { name = "BlobMesh" };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;
    }

    private bool InBounds(int x, int y, int z)
    {
        return x >= 0 && y >= 0 && z >= 0 &&
               x < size.x && y < size.y && z < size.z;
    }

    private float GetOffset(float v1, float v2)
    {
        float delta = v2 - v1;
        if (Mathf.Abs(delta) < 0.00001f)
        {
            return 0.5f;
        }

        return (isoLevel - v1) / delta;
    }

    private float SampleField(float x, float y, float z)
    {
        if (voxels == null)
        {
            return 0f;
        }

        if (x < 0f || y < 0f || z < 0f ||
            x > size.x - 1f || y > size.y - 1f || z > size.z - 1f)
        {
            return 0f;
        }

        float fx = x;
        float fy = y;
        float fz = z;

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, size.x - 1);
        int y1 = Mathf.Min(y0 + 1, size.y - 1);
        int z1 = Mathf.Min(z0 + 1, size.z - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        float c000 = voxels[x0, y0, z0];
        float c100 = voxels[x1, y0, z0];
        float c010 = voxels[x0, y1, z0];
        float c110 = voxels[x1, y1, z0];
        float c001 = voxels[x0, y0, z1];
        float c101 = voxels[x1, y0, z1];
        float c011 = voxels[x0, y1, z1];
        float c111 = voxels[x1, y1, z1];

        float c00 = Mathf.Lerp(c000, c100, tx);
        float c10 = Mathf.Lerp(c010, c110, tx);
        float c01 = Mathf.Lerp(c001, c101, tx);
        float c11 = Mathf.Lerp(c011, c111, tx);
        float c0 = Mathf.Lerp(c00, c10, ty);
        float c1 = Mathf.Lerp(c01, c11, ty);
        return Mathf.Lerp(c0, c1, tz);
    }

    private float SampleFieldClamped(float x, float y, float z)
    {
        float cx = Mathf.Clamp(x, 0f, size.x - 1f);
        float cy = Mathf.Clamp(y, 0f, size.y - 1f);
        float cz = Mathf.Clamp(z, 0f, size.z - 1f);
        return SampleField(cx, cy, cz);
    }

    private float SampleScanField(float x, float y, float z)
    {
        if (scanVoxels == null)
        {
            return 0f;
        }

        if (x < 0f || y < 0f || z < 0f ||
            x > size.x - 1f || y > size.y - 1f || z > size.z - 1f)
        {
            return 0f;
        }

        float fx = x;
        float fy = y;
        float fz = z;

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, size.x - 1);
        int y1 = Mathf.Min(y0 + 1, size.y - 1);
        int z1 = Mathf.Min(z0 + 1, size.z - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        float c000 = scanVoxels[x0, y0, z0];
        float c100 = scanVoxels[x1, y0, z0];
        float c010 = scanVoxels[x0, y1, z0];
        float c110 = scanVoxels[x1, y1, z0];
        float c001 = scanVoxels[x0, y0, z1];
        float c101 = scanVoxels[x1, y0, z1];
        float c011 = scanVoxels[x0, y1, z1];
        float c111 = scanVoxels[x1, y1, z1];

        float c00 = Mathf.Lerp(c000, c100, tx);
        float c10 = Mathf.Lerp(c010, c110, tx);
        float c01 = Mathf.Lerp(c001, c101, tx);
        float c11 = Mathf.Lerp(c011, c111, tx);
        float c0 = Mathf.Lerp(c00, c10, ty);
        float c1 = Mathf.Lerp(c01, c11, ty);
        return Mathf.Lerp(c0, c1, tz);
    }

    private Color SampleScanColor(float x, float y, float z)
    {
        if (scanColors == null)
        {
            return Color.black;
        }

        if (x < 0f || y < 0f || z < 0f ||
            x > size.x - 1f || y > size.y - 1f || z > size.z - 1f)
        {
            return Color.black;
        }

        float fx = x;
        float fy = y;
        float fz = z;

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, size.x - 1);
        int y1 = Mathf.Min(y0 + 1, size.y - 1);
        int z1 = Mathf.Min(z0 + 1, size.z - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        Color c000 = scanColors[x0, y0, z0];
        Color c100 = scanColors[x1, y0, z0];
        Color c010 = scanColors[x0, y1, z0];
        Color c110 = scanColors[x1, y1, z0];
        Color c001 = scanColors[x0, y0, z1];
        Color c101 = scanColors[x1, y0, z1];
        Color c011 = scanColors[x0, y1, z1];
        Color c111 = scanColors[x1, y1, z1];

        Color c00 = Color.Lerp(c000, c100, tx);
        Color c10 = Color.Lerp(c010, c110, tx);
        Color c01 = Color.Lerp(c001, c101, tx);
        Color c11 = Color.Lerp(c011, c111, tx);
        Color c0 = Color.Lerp(c00, c10, ty);
        Color c1 = Color.Lerp(c01, c11, ty);
        return Color.Lerp(c0, c1, tz);
    }

    private Color SampleColorField(float x, float y, float z)
    {
        if (colors == null || size.x <= 0 || size.y <= 0 || size.z <= 0)
        {
            return Color.black;
        }

        float fx = x;
        float fy = y;
        float fz = z;

        int x0 = Mathf.FloorToInt(Mathf.Clamp(fx, 0f, size.x - 1f));
        int y0 = Mathf.FloorToInt(Mathf.Clamp(fy, 0f, size.y - 1f));
        int z0 = Mathf.FloorToInt(Mathf.Clamp(fz, 0f, size.z - 1f));
        int x1 = Mathf.Clamp(x0 + 1, 0, size.x - 1);
        int y1 = Mathf.Clamp(y0 + 1, 0, size.y - 1);
        int z1 = Mathf.Clamp(z0 + 1, 0, size.z - 1);

        float tx = Mathf.Clamp01(fx - x0);
        float ty = Mathf.Clamp01(fy - y0);
        float tz = Mathf.Clamp01(fz - z0);

        Color c000 = colors[x0, y0, z0];
        Color c100 = colors[x1, y0, z0];
        Color c010 = colors[x0, y1, z0];
        Color c110 = colors[x1, y1, z0];
        Color c001 = colors[x0, y0, z1];
        Color c101 = colors[x1, y0, z1];
        Color c011 = colors[x0, y1, z1];
        Color c111 = colors[x1, y1, z1];

        Color c00 = Color.Lerp(c000, c100, tx);
        Color c10 = Color.Lerp(c010, c110, tx);
        Color c01 = Color.Lerp(c001, c101, tx);
        Color c11 = Color.Lerp(c011, c111, tx);
        Color c0 = Color.Lerp(c00, c10, ty);
        Color c1 = Color.Lerp(c01, c11, ty);
        return Color.Lerp(c0, c1, tz);
    }

    private Color SampleColorByDensity(float x, float y, float z)
    {
        if (colors == null || voxels == null || size.x <= 0 || size.y <= 0 || size.z <= 0)
        {
            return Color.black;
        }

        float fx = Mathf.Clamp(x, 0f, size.x - 1f);
        float fy = Mathf.Clamp(y, 0f, size.y - 1f);
        float fz = Mathf.Clamp(z, 0f, size.z - 1f);

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, size.x - 1);
        int y1 = Mathf.Min(y0 + 1, size.y - 1);
        int z1 = Mathf.Min(z0 + 1, size.z - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        float w000 = Mathf.Max(0f, voxels[x0, y0, z0] - isoLevel);
        float w100 = Mathf.Max(0f, voxels[x1, y0, z0] - isoLevel);
        float w010 = Mathf.Max(0f, voxels[x0, y1, z0] - isoLevel);
        float w110 = Mathf.Max(0f, voxels[x1, y1, z0] - isoLevel);
        float w001 = Mathf.Max(0f, voxels[x0, y0, z1] - isoLevel);
        float w101 = Mathf.Max(0f, voxels[x1, y0, z1] - isoLevel);
        float w011 = Mathf.Max(0f, voxels[x0, y1, z1] - isoLevel);
        float w111 = Mathf.Max(0f, voxels[x1, y1, z1] - isoLevel);

        float wx0 = Mathf.Lerp(w000, w100, tx);
        float wx1 = Mathf.Lerp(w010, w110, tx);
        float wx2 = Mathf.Lerp(w001, w101, tx);
        float wx3 = Mathf.Lerp(w011, w111, tx);
        float wy0 = Mathf.Lerp(wx0, wx1, ty);
        float wy1 = Mathf.Lerp(wx2, wx3, ty);
        float weight = Mathf.Lerp(wy0, wy1, tz);

        if (weight <= 0f)
        {
            return SampleColorField(x, y, z);
        }

        Color c000 = colors[x0, y0, z0];
        Color c100 = colors[x1, y0, z0];
        Color c010 = colors[x0, y1, z0];
        Color c110 = colors[x1, y1, z0];
        Color c001 = colors[x0, y0, z1];
        Color c101 = colors[x1, y0, z1];
        Color c011 = colors[x0, y1, z1];
        Color c111 = colors[x1, y1, z1];

        Color c00 = Color.Lerp(c000, c100, tx);
        Color c10 = Color.Lerp(c010, c110, tx);
        Color c01 = Color.Lerp(c001, c101, tx);
        Color c11 = Color.Lerp(c011, c111, tx);
        Color c0 = Color.Lerp(c00, c10, ty);
        Color c1 = Color.Lerp(c01, c11, ty);
        return Color.Lerp(c0, c1, tz);
    }

    private float GetVoxelValueSafe(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
        {
            return 0f;
        }

        return voxels[x, y, z];
    }

    private Color GetColorSafe(int x, int y, int z)
    {
        if (colors == null || size.x <= 0 || size.y <= 0 || size.z <= 0)
        {
            return Color.black;
        }

        int cx = Mathf.Clamp(x, 0, size.x - 1);
        int cy = Mathf.Clamp(y, 0, size.y - 1);
        int cz = Mathf.Clamp(z, 0, size.z - 1);
        return colors[cx, cy, cz];
    }

    private Vector3 ComputeNormal(Vector3 position)
    {
        float safeVoxelSize = Mathf.Max(0.0001f, voxelSize);
        Vector3 p = position / safeVoxelSize + GetGridCenter();
        const float epsilon = 0.5f;
        float maxX = size.x - 1f;
        float maxY = size.y - 1f;
        float maxZ = size.z - 1f;

        float dx;
        if (p.x - epsilon < 0f || p.x + epsilon > maxX)
        {
            dx = SampleFieldClamped(p.x + epsilon, p.y, p.z) - SampleFieldClamped(p.x - epsilon, p.y, p.z);
        }
        else
        {
            dx = SampleField(p.x + epsilon, p.y, p.z) - SampleField(p.x - epsilon, p.y, p.z);
        }

        float dy;
        if (p.y - epsilon < 0f || p.y + epsilon > maxY)
        {
            dy = SampleFieldClamped(p.x, p.y + epsilon, p.z) - SampleFieldClamped(p.x, p.y - epsilon, p.z);
        }
        else
        {
            dy = SampleField(p.x, p.y + epsilon, p.z) - SampleField(p.x, p.y - epsilon, p.z);
        }

        float dz;
        if (p.z - epsilon < 0f || p.z + epsilon > maxZ)
        {
            dz = SampleFieldClamped(p.x, p.y, p.z + epsilon) - SampleFieldClamped(p.x, p.y, p.z - epsilon);
        }
        else
        {
            dz = SampleField(p.x, p.y, p.z + epsilon) - SampleField(p.x, p.y, p.z - epsilon);
        }
        Vector3 n = new Vector3(dx, dy, dz);
        if (n.sqrMagnitude < 0.000001f)
        {
            return Vector3.up;
        }

        return -n.normalized;
    }

    private Vector3 GetGridCenter()
    {
        return new Vector3(size.x - 1, size.y - 1, size.z - 1) * 0.5f;
    }

    private float ComputeSimilarity(float rotationDegrees, bool includeColors)
    {
        int totalVoxels = voxels != null ? voxels.Length : 0;
        if (totalVoxels <= 0)
        {
            return 0f;
        }

        int filledCount = CountFilledVoxels(voxels, isoLevel);
        int scanFilledCount = CountFilledVoxels(scanVoxels, isoLevel);
        return Mathf.Abs(filledCount - scanFilledCount) / (float)totalVoxels;
    }

    private float ComputeSimilarity(float rotationDegrees, float scale, int yOffset, bool includeColors)
    {
        int totalVoxels = voxels != null ? voxels.Length : 0;
        if (totalVoxels <= 0)
        {
            return 0f;
        }

        int filledCount = CountFilledVoxels(voxels, isoLevel);
        int scanFilledCount = CountFilledVoxels(scanVoxels, isoLevel);
        return Mathf.Abs(filledCount - scanFilledCount) / (float)totalVoxels;
    }

    private int CountFilledVoxels(float[,,] grid, float threshold)
    {
        if (grid == null)
        {
            return 0;
        }

        int count = 0;
        int maxX = grid.GetLength(0);
        int maxY = grid.GetLength(1);
        int maxZ = grid.GetLength(2);
        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                for (int z = 0; z < maxZ; z++)
                {
                    if (grid[x, y, z] >= threshold)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private float GetSignedDistance(Vector3 worldPoint, Collider collider, SphereCollider probe)
    {
        if (collider == null || probe == null)
        {
            Vector3 closestFallback = collider != null ? collider.ClosestPoint(worldPoint) : worldPoint;
            float distanceFallback = Vector3.Distance(worldPoint, closestFallback);
            bool insideFallback = collider != null && (closestFallback - worldPoint).sqrMagnitude < 0.000001f;
            return insideFallback ? -distanceFallback : distanceFallback;
        }

        MeshCollider meshCollider = collider as MeshCollider;
        bool isNonConvexMesh = meshCollider != null && !meshCollider.convex;
        float distance = isNonConvexMesh
            ? GetDistanceToNonConvexMesh(worldPoint, collider)
            : Vector3.Distance(worldPoint, collider.ClosestPoint(worldPoint));

        bool inside = false;
        float penetrationDistance = 0f;
        if (isNonConvexMesh)
        {
            inside = IsPointInsideNonConvexMesh(worldPoint, collider);
        }
        else
        {
            bool penetrationInside = Physics.ComputePenetration(
                    probe,
                    probe.transform.position,
                    probe.transform.rotation,
                    collider,
                    collider.transform.position,
                    collider.transform.rotation,
                    out _,
                    out penetrationDistance);

            inside = penetrationInside || distance <= 0.0001f;
        }
        if (inside)
        {
            float insideDistance = penetrationDistance > 0f ? penetrationDistance : Mathf.Max(distance, voxelSize * 0.5f);
            return -insideDistance;
        }

        return distance;
    }

    private float GetSignedDistanceToMesh(Vector3 worldPoint, Vector3[] vertices, int[] triangles)
    {
        if (vertices == null || triangles == null || triangles.Length == 0)
        {
            return 0f;
        }

        float minDistSq = float.PositiveInfinity;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            float d = PointTriangleDistanceSquared(worldPoint, a, b, c);
            if (d < minDistSq)
            {
                minDistSq = d;
            }
        }

        float distance = Mathf.Sqrt(minDistSq);
        bool inside = IsPointInsideMesh(worldPoint, vertices, triangles);
        return inside ? -distance : distance;
    }

    private struct TriangleData
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
        public Vector3 min;
        public Vector3 max;
    }

    private struct MeshAccel
    {
        public Vector3 boundsMin;
        public Vector3 boundsMax;
        public Vector3 cellSize;
        public int nx;
        public int ny;
        public int nz;
        public TriangleData[] tris;
        public int[] cellOffsets;
        public int[] cellCounts;
        public int[] cellTris;
    }

    private MeshAccel BuildMeshAccel(Vector3[] worldVertices, int[] triangles)
    {
        MeshAccel accel = new MeshAccel();
        if (worldVertices == null || worldVertices.Length == 0 || triangles == null || triangles.Length == 0)
        {
            return accel;
        }

        Vector3 min = worldVertices[0];
        Vector3 max = worldVertices[0];
        for (int i = 1; i < worldVertices.Length; i++)
        {
            min = Vector3.Min(min, worldVertices[i]);
            max = Vector3.Max(max, worldVertices[i]);
        }

        accel.boundsMin = min;
        accel.boundsMax = max;
        accel.cellSize = new Vector3(
            Mathf.Max(0.0001f, voxelSize),
            Mathf.Max(0.0001f, voxelSize),
            Mathf.Max(0.0001f, voxelSize)
        );
        accel.nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / accel.cellSize.x));
        accel.ny = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / accel.cellSize.y));
        accel.nz = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / accel.cellSize.z));

        int triCount = triangles.Length / 3;
        accel.tris = new TriangleData[triCount];
        for (int i = 0; i < triCount; i++)
        {
            int t = i * 3;
            Vector3 a = worldVertices[triangles[t]];
            Vector3 b = worldVertices[triangles[t + 1]];
            Vector3 c = worldVertices[triangles[t + 2]];
            TriangleData data = new TriangleData
            {
                a = a,
                b = b,
                c = c,
                min = Vector3.Min(a, Vector3.Min(b, c)),
                max = Vector3.Max(a, Vector3.Max(b, c))
            };
            accel.tris[i] = data;
        }

        int cellCount = accel.nx * accel.ny * accel.nz;
        List<int>[] cellLists = new List<int>[cellCount];
        for (int i = 0; i < triCount; i++)
        {
            TriangleData tri = accel.tris[i];
            Vector3 minCell = WorldToCell(tri.min, accel);
            Vector3 maxCell = WorldToCell(tri.max, accel);
            int x0 = Mathf.Clamp((int)minCell.x, 0, accel.nx - 1);
            int y0 = Mathf.Clamp((int)minCell.y, 0, accel.ny - 1);
            int z0 = Mathf.Clamp((int)minCell.z, 0, accel.nz - 1);
            int x1 = Mathf.Clamp((int)maxCell.x, 0, accel.nx - 1);
            int y1 = Mathf.Clamp((int)maxCell.y, 0, accel.ny - 1);
            int z1 = Mathf.Clamp((int)maxCell.z, 0, accel.nz - 1);

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        int idx = CellIndex(x, y, z, accel);
                        if (cellLists[idx] == null)
                        {
                            cellLists[idx] = new List<int>();
                        }
                        cellLists[idx].Add(i);
                    }
                }
            }
        }

        accel.cellOffsets = new int[cellCount];
        accel.cellCounts = new int[cellCount];
        int total = 0;
        for (int i = 0; i < cellCount; i++)
        {
            int count = cellLists[i] != null ? cellLists[i].Count : 0;
            accel.cellOffsets[i] = total;
            accel.cellCounts[i] = count;
            total += count;
        }

        accel.cellTris = new int[total];
        int cursor = 0;
        for (int i = 0; i < cellCount; i++)
        {
            List<int> list = cellLists[i];
            if (list == null)
            {
                continue;
            }

            for (int j = 0; j < list.Count; j++)
            {
                accel.cellTris[cursor++] = list[j];
            }
        }

        return accel;
    }

    private static Vector3[] TransformVertices(Vector3[] localVertices, Matrix4x4 localToWorld)
    {
        if (localVertices == null || localVertices.Length == 0)
        {
            return System.Array.Empty<Vector3>();
        }

        Vector3[] worldVertices = new Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++)
        {
            worldVertices[i] = localToWorld.MultiplyPoint3x4(localVertices[i]);
        }

        return worldVertices;
    }

    private static Bounds ComputeBounds(Vector3[] points)
    {
        if (points == null || points.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Bounds bounds = new Bounds(points[0], Vector3.zero);
        for (int i = 1; i < points.Length; i++)
        {
            bounds.Encapsulate(points[i]);
        }

        return bounds;
    }

    private Vector3 WorldToCell(Vector3 world, MeshAccel accel)
    {
        Vector3 local = world - accel.boundsMin;
        return new Vector3(
            local.x / accel.cellSize.x,
            local.y / accel.cellSize.y,
            local.z / accel.cellSize.z
        );
    }

    private int CellIndex(int x, int y, int z, MeshAccel accel)
    {
        return (x * accel.ny + y) * accel.nz + z;
    }

    private float GetSignedDistanceToMeshAccelerated(
        Vector3 worldPoint,
        MeshAccel accel,
        int[] triVisit,
        ref int visitMark)
    {
        if (accel.tris == null || accel.tris.Length == 0)
        {
            return 0f;
        }

        float minDistSq = float.PositiveInfinity;
        int cx = Mathf.Clamp((int)WorldToCell(worldPoint, accel).x, 0, accel.nx - 1);
        int cy = Mathf.Clamp((int)WorldToCell(worldPoint, accel).y, 0, accel.ny - 1);
        int cz = Mathf.Clamp((int)WorldToCell(worldPoint, accel).z, 0, accel.nz - 1);

        int maxRadius = Mathf.Max(accel.nx, Mathf.Max(accel.ny, accel.nz));
        for (int r = 0; r <= maxRadius; r++)
        {
            float shellMin = float.PositiveInfinity;
            int x0 = Mathf.Max(0, cx - r);
            int x1 = Mathf.Min(accel.nx - 1, cx + r);
            int y0 = Mathf.Max(0, cy - r);
            int y1 = Mathf.Min(accel.ny - 1, cy + r);
            int z0 = Mathf.Max(0, cz - r);
            int z1 = Mathf.Min(accel.nz - 1, cz + r);

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        if (Mathf.Abs(x - cx) != r && Mathf.Abs(y - cy) != r && Mathf.Abs(z - cz) != r)
                        {
                            continue;
                        }

                        Vector3 cellMin = accel.boundsMin + new Vector3(x * accel.cellSize.x, y * accel.cellSize.y, z * accel.cellSize.z);
                        Vector3 cellMax = cellMin + accel.cellSize;
                        float cellDistSq = DistanceToAabbSquared(worldPoint, cellMin, cellMax);
                        shellMin = Mathf.Min(shellMin, cellDistSq);
                        if (cellDistSq > minDistSq)
                        {
                            continue;
                        }

                        int idx = CellIndex(x, y, z, accel);
                        int count = accel.cellCounts[idx];
                        if (count == 0)
                        {
                            continue;
                        }

                        int offset = accel.cellOffsets[idx];
                        for (int i = 0; i < count; i++)
                        {
                            int triIndex = accel.cellTris[offset + i];
                            if (triVisit[triIndex] == visitMark)
                            {
                                continue;
                            }

                            triVisit[triIndex] = visitMark;
                            TriangleData tri = accel.tris[triIndex];
                            float d = PointTriangleDistanceSquared(worldPoint, tri.a, tri.b, tri.c);
                            if (d < minDistSq)
                            {
                                minDistSq = d;
                            }
                        }
                    }
                }
            }

            if (shellMin > minDistSq)
            {
                break;
            }
        }

        visitMark++;
        if (visitMark == int.MaxValue)
        {
            System.Array.Clear(triVisit, 0, triVisit.Length);
            visitMark = 1;
        }

        float distance = Mathf.Sqrt(minDistSq);
        bool inside = IsPointInsideMeshAccelerated(worldPoint, accel, triVisit, ref visitMark);
        return inside ? -distance : distance;
    }

    private bool IsPointInsideMeshAccelerated(
        Vector3 point,
        MeshAccel accel,
        int[] triVisit,
        ref int visitMark)
    {
        Vector3 dir = new Vector3(1f, 0.123f, 0.037f).normalized;
        Vector3 origin = point + dir * 0.0001f;
        if (!RayAabbIntersect(origin, dir, accel.boundsMin, accel.boundsMax, out float tMin, out float tMax))
        {
            return false;
        }

        int hits = 0;
        float t = Mathf.Max(0f, tMin);
        Vector3 p = origin + dir * t;
        Vector3 cell = WorldToCell(p, accel);
        int x = Mathf.Clamp((int)cell.x, 0, accel.nx - 1);
        int y = Mathf.Clamp((int)cell.y, 0, accel.ny - 1);
        int z = Mathf.Clamp((int)cell.z, 0, accel.nz - 1);

        Vector3 nextBoundary = accel.boundsMin + new Vector3((x + 1) * accel.cellSize.x, (y + 1) * accel.cellSize.y, (z + 1) * accel.cellSize.z);
        Vector3 step = new Vector3(Mathf.Sign(dir.x), Mathf.Sign(dir.y), Mathf.Sign(dir.z));
        Vector3 tMaxVec = new Vector3(
            dir.x != 0f ? (nextBoundary.x - p.x) / dir.x : float.PositiveInfinity,
            dir.y != 0f ? (nextBoundary.y - p.y) / dir.y : float.PositiveInfinity,
            dir.z != 0f ? (nextBoundary.z - p.z) / dir.z : float.PositiveInfinity
        );
        Vector3 tDelta = new Vector3(
            dir.x != 0f ? accel.cellSize.x / Mathf.Abs(dir.x) : float.PositiveInfinity,
            dir.y != 0f ? accel.cellSize.y / Mathf.Abs(dir.y) : float.PositiveInfinity,
            dir.z != 0f ? accel.cellSize.z / Mathf.Abs(dir.z) : float.PositiveInfinity
        );

        while (t <= tMax)
        {
            int idx = CellIndex(x, y, z, accel);
            int count = accel.cellCounts[idx];
            int offset = accel.cellOffsets[idx];
            for (int i = 0; i < count; i++)
            {
                int triIndex = accel.cellTris[offset + i];
                if (triVisit[triIndex] == visitMark)
                {
                    continue;
                }

                triVisit[triIndex] = visitMark;
                TriangleData tri = accel.tris[triIndex];
                if (RayIntersectsTriangle(origin, dir, tri.a, tri.b, tri.c, out float hitT))
                {
                    if (hitT > 0.0001f)
                    {
                        hits++;
                    }
                }
            }

            if (tMaxVec.x < tMaxVec.y)
            {
                if (tMaxVec.x < tMaxVec.z)
                {
                    x += (int)step.x;
                    t = tMaxVec.x;
                    tMaxVec.x += tDelta.x;
                }
                else
                {
                    z += (int)step.z;
                    t = tMaxVec.z;
                    tMaxVec.z += tDelta.z;
                }
            }
            else
            {
                if (tMaxVec.y < tMaxVec.z)
                {
                    y += (int)step.y;
                    t = tMaxVec.y;
                    tMaxVec.y += tDelta.y;
                }
                else
                {
                    z += (int)step.z;
                    t = tMaxVec.z;
                    tMaxVec.z += tDelta.z;
                }
            }

            if (x < 0 || y < 0 || z < 0 || x >= accel.nx || y >= accel.ny || z >= accel.nz)
            {
                break;
            }
        }

        visitMark++;
        if (visitMark == int.MaxValue)
        {
            System.Array.Clear(triVisit, 0, triVisit.Length);
            visitMark = 1;
        }

        return (hits % 2) == 1;
    }

    private bool RayAabbIntersect(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float tMin, out float tMax)
    {
        tMin = 0f;
        tMax = float.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? origin.x : (i == 1 ? origin.y : origin.z);
            float d = i == 0 ? dir.x : (i == 1 ? dir.y : dir.z);
            float mn = i == 0 ? min.x : (i == 1 ? min.y : min.z);
            float mx = i == 0 ? max.x : (i == 1 ? max.y : max.z);

            if (Mathf.Abs(d) < 0.000001f)
            {
                if (o < mn || o > mx)
                {
                    return false;
                }
                continue;
            }

            float inv = 1f / d;
            float t1 = (mn - o) * inv;
            float t2 = (mx - o) * inv;
            if (t1 > t2)
            {
                float temp = t1;
                t1 = t2;
                t2 = temp;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        return true;
    }

    private float DistanceToAabbSquared(Vector3 point, Vector3 min, Vector3 max)
    {
        float dx = point.x < min.x ? min.x - point.x : (point.x > max.x ? point.x - max.x : 0f);
        float dy = point.y < min.y ? min.y - point.y : (point.y > max.y ? point.y - max.y : 0f);
        float dz = point.z < min.z ? min.z - point.z : (point.z > max.z ? point.z - max.z : 0f);
        return dx * dx + dy * dy + dz * dz;
    }

    private bool IsPointInsideMesh(Vector3 point, Vector3[] vertices, int[] triangles)
    {
        Vector3 dir = new Vector3(1f, 0.123f, 0.037f).normalized;
        Vector3 origin = point + dir * 0.0001f;
        int hits = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            if (RayIntersectsTriangle(origin, dir, a, b, c, out float t))
            {
                if (t > 0.0001f)
                {
                    hits++;
                }
            }
        }

        return (hits % 2) == 1;
    }

    private bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        const float epsilon = 0.000001f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 pvec = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, pvec);
        if (det > -epsilon && det < epsilon)
        {
            t = 0f;
            return false;
        }

        float invDet = 1f / det;
        Vector3 tvec = origin - a;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
        {
            t = 0f;
            return false;
        }

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(direction, qvec) * invDet;
        if (v < 0f || u + v > 1f)
        {
            t = 0f;
            return false;
        }

        t = Vector3.Dot(edge2, qvec) * invDet;
        return t > epsilon;
    }

    private float PointTriangleDistanceSquared(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return ap.sqrMagnitude;
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return bp.sqrMagnitude;
        }

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            Vector3 proj = a + v * ab;
            return (p - proj).sqrMagnitude;
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return cp.sqrMagnitude;
        }

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            Vector3 proj = a + w * ac;
            return (p - proj).sqrMagnitude;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            Vector3 proj = b + w * (c - b);
            return (p - proj).sqrMagnitude;
        }

        Vector3 normal = Vector3.Cross(ab, ac).normalized;
        float distance = Mathf.Abs(Vector3.Dot(p - a, normal));
        return distance * distance;
    }

    private float GetDistanceToNonConvexMesh(Vector3 worldPoint, Collider collider)
    {
        if (collider == null)
        {
            return 0f;
        }

        Bounds bounds = collider.bounds;
        int mask = 1 << collider.gameObject.layer;
        float maxDistance = bounds.size.magnitude + 1f;
        Vector3[] dirs =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        float minHit = maxDistance;
        Vector3 toCenter = bounds.center - worldPoint;
        float toCenterDistance = toCenter.magnitude;
        if (toCenterDistance > 0.0001f)
        {
            RaycastHit[] centerHits = Physics.RaycastAll(worldPoint, toCenter / toCenterDistance, toCenterDistance + maxDistance, mask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < centerHits.Length; h++)
            {
                if (centerHits[h].collider == collider)
                {
                    minHit = Mathf.Min(minHit, centerHits[h].distance);
                }
            }
        }

        for (int i = 0; i < dirs.Length; i++)
        {
            RaycastHit[] hits = Physics.RaycastAll(worldPoint, dirs[i], maxDistance, mask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < hits.Length; h++)
            {
                if (hits[h].collider == collider)
                {
                    minHit = Mathf.Min(minHit, hits[h].distance);
                }
            }
        }

        if (!bounds.Contains(worldPoint) && minHit >= maxDistance)
        {
            Vector3 closest = bounds.ClosestPoint(worldPoint);
            return Vector3.Distance(worldPoint, closest);
        }

        return minHit;
    }

    private bool IsPointInsideNonConvexMesh(Vector3 worldPoint, Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        Bounds bounds = collider.bounds;
        if (!bounds.Contains(worldPoint))
        {
            return false;
        }

        int mask = 1 << collider.gameObject.layer;
        float extent = bounds.extents.magnitude + 1f;
        Vector3[] directions =
        {
            Vector3.right,
            Vector3.up,
            Vector3.forward
        };

        int insideCount = 0;
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 dir = directions[i];
            Vector3 origin = bounds.center - dir * extent;
            Vector3 toPoint = worldPoint - origin;
            float distanceToPoint = toPoint.magnitude;
            if (distanceToPoint <= 0.0001f)
            {
                insideCount++;
                continue;
            }

            int hits = CountRayHitsToPoint(origin, toPoint / distanceToPoint, distanceToPoint, collider, mask);
            if ((hits % 2) == 1)
            {
                insideCount++;
            }
        }

        return insideCount >= 2;
    }

    private int CountRayHitsToPoint(Vector3 origin, Vector3 direction, float distance, Collider targetCollider, int mask)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, mask, QueryTriggerInteraction.Ignore);
        int count = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == targetCollider)
            {
                count++;
            }
        }

        return count;
    }

    private Color SampleScanColor(
        Vector3 worldPoint,
        Transform targetTransform,
        Vector3[] vertices,
        Color[] vertexColors,
        bool hasVertexColors,
        Color fallback)
    {
        Vector3 local = targetTransform.InverseTransformPoint(worldPoint);
        return SampleScanColorLocal(local, vertices, vertexColors, hasVertexColors, fallback);
    }

    private Color SampleScanColorLocal(
        Vector3 localPoint,
        Vector3[] vertices,
        Color[] vertexColors,
        bool hasVertexColors,
        Color fallback)
    {
        if (!hasVertexColors || vertices.Length == 0)
        {
            return fallback;
        }

        float best = float.PositiveInfinity;
        int bestIndex = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float d = (vertices[i] - localPoint).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestIndex = i;
            }
        }

        return vertexColors[bestIndex];
    }

    private void ScanObjectToGridRaycastToCenter(
        Collider targetCollider,
        Vector3[] vertices,
        Color[] vertexColors,
        bool hasVertexColors,
        Color fallbackColor,
        Vector3 gridCenter,
        Matrix4x4 blobToWorld,
        Matrix4x4 targetWorldToLocal,
        Vector3 scanOffset)
    {
        Bounds bounds = targetCollider.bounds;
        float epsilon = Mathf.Max(0.0001f, voxelSize * 0.05f);
        Vector3 centerPoint = bounds.center;
        float maxRayDistance = Mathf.Max(scanDistanceMax, bounds.extents.magnitude) * 2f;
        GameObject probeObject = new GameObject("ScanInsideProbe");
        probeObject.hideFlags = HideFlags.HideAndDontSave;
        SphereCollider probe = probeObject.AddComponent<SphereCollider>();
        probe.isTrigger = true;
        probe.radius = Mathf.Max(epsilon, voxelSize * 0.15f);

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    Vector3 local = new Vector3(x, y, z) - gridCenter;
                    Vector3 world = blobToWorld.MultiplyPoint3x4(local * voxelSize);
                    Vector3 scanWorld = world + scanOffset;

                    bool inside = false;
                    float penetrationDistance = 0f;
                    float insideOffset = voxelSize * 0.25f;
                    Vector3[] sampleOffsets =
                    {
                        Vector3.zero,
                        new Vector3(insideOffset, 0f, 0f),
                        new Vector3(-insideOffset, 0f, 0f),
                        new Vector3(0f, insideOffset, 0f),
                        new Vector3(0f, -insideOffset, 0f),
                        new Vector3(0f, 0f, insideOffset),
                        new Vector3(0f, 0f, -insideOffset)
                    };
                    for (int s = 0; s < sampleOffsets.Length; s++)
                    {
                        if (Physics.ComputePenetration(
                            probe,
                            scanWorld + sampleOffsets[s],
                            Quaternion.identity,
                            targetCollider,
                            targetCollider.transform.position,
                            targetCollider.transform.rotation,
                            out _,
                            out float samplePenetration) && samplePenetration > 0f)
                        {
                            inside = true;
                            penetrationDistance = Mathf.Max(penetrationDistance, samplePenetration);
                            break;
                        }
                    }

                    float surfaceDistance = GetRaycastSurfaceDistance(targetCollider, scanWorld, centerPoint, maxRayDistance, epsilon);
                    surfaceDistance = Mathf.Min(surfaceDistance, scanDistanceMax);
                    float signedDistance = inside ? -Mathf.Max(surfaceDistance, penetrationDistance) : surfaceDistance;

                    float value = Mathf.Clamp01(isoLevel - (signedDistance / (2f * scanDistanceMax)));
                    scanVoxels[x, y, z] = value;
                    Vector3 scanLocalColor = targetWorldToLocal.MultiplyPoint3x4(scanWorld);
                    scanColors[x, y, z] = SampleScanColorLocal(
                        scanLocalColor,
                        vertices,
                        vertexColors,
                        hasVertexColors,
                        fallbackColor
                    );
                }
            }
        }

        if (Application.isPlaying)
        {
            Destroy(probeObject);
        }
        else
        {
            DestroyImmediate(probeObject);
        }
    }

    private float GetRaycastSurfaceDistance(
        Collider targetCollider,
        Vector3 origin,
        Vector3 centerPoint,
        float maxDistance,
        float epsilon)
    {
        Vector3 toCenter = centerPoint - origin;
        float distance = toCenter.magnitude;
        if (distance <= epsilon)
        {
            return 0f;
        }

        Vector3 dir = toCenter / distance;
        float rayDistance = Mathf.Min(maxDistance, distance + maxDistance * 0.5f);
        return targetCollider.Raycast(new Ray(origin, dir), out RaycastHit hit, rayDistance)
            ? hit.distance
            : maxDistance;
    }

    private static Vector3 GetInsidePoint(Collider targetCollider, Bounds bounds, float epsilon)
    {
        Vector3 candidate = bounds.center;
        if (IsPointInsideCollider(targetCollider, candidate, epsilon))
        {
            return candidate;
        }

        Vector3 toTarget = targetCollider.transform.position - bounds.center;
        float distance = toTarget.magnitude;
        if (distance > epsilon)
        {
            Vector3 dir = toTarget / distance;
            if (targetCollider.Raycast(new Ray(bounds.center, dir), out RaycastHit hit, distance * 2f))
            {
                if (TryNudgeInside(targetCollider, hit, epsilon, out Vector3 insidePoint))
                {
                    return insidePoint;
                }
            }
        }

        float maxDistance = bounds.extents.magnitude * 2.5f;
        Vector3[] dirs =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            (Vector3.right + Vector3.up).normalized,
            (Vector3.right + Vector3.down).normalized,
            (Vector3.left + Vector3.up).normalized,
            (Vector3.left + Vector3.down).normalized,
            (Vector3.forward + Vector3.up).normalized,
            (Vector3.forward + Vector3.down).normalized,
            (Vector3.back + Vector3.up).normalized,
            (Vector3.back + Vector3.down).normalized
        };

        Vector3 origin = bounds.center;
        for (int i = 0; i < dirs.Length; i++)
        {
            if (targetCollider.Raycast(new Ray(origin, dirs[i]), out RaycastHit hit, maxDistance))
            {
                if (TryNudgeInside(targetCollider, hit, epsilon, out Vector3 insidePoint))
                {
                    return insidePoint;
                }
            }
        }

        origin = targetCollider.transform.position;
        for (int i = 0; i < dirs.Length; i++)
        {
            if (targetCollider.Raycast(new Ray(origin, dirs[i]), out RaycastHit hit, maxDistance))
            {
                if (TryNudgeInside(targetCollider, hit, epsilon, out Vector3 insidePoint))
                {
                    return insidePoint;
                }
            }
        }

        return targetCollider.transform.position;
    }

    private static bool TryNudgeInside(Collider targetCollider, RaycastHit hit, float epsilon, out Vector3 insidePoint)
    {
        insidePoint = hit.point - hit.normal * epsilon;
        if (IsPointInsideCollider(targetCollider, insidePoint, epsilon))
        {
            return true;
        }

        insidePoint = hit.point + hit.normal * epsilon;
        if (IsPointInsideCollider(targetCollider, insidePoint, epsilon))
        {
            return true;
        }

        return false;
    }

    private static bool IsPointInsideCollider(Collider targetCollider, Vector3 point, float epsilon)
    {
        bool canUseClosestPoint = true;
        if (targetCollider is MeshCollider meshCollider && !meshCollider.convex)
        {
            canUseClosestPoint = false;
        }

        if (canUseClosestPoint)
        {
            Vector3 closest = targetCollider.ClosestPoint(point);
            if ((closest - point).sqrMagnitude <= epsilon * epsilon)
            {
                return true;
            }
        }

        GameObject probeObject = new GameObject("ScanInsideProbe");
        probeObject.hideFlags = HideFlags.HideAndDontSave;
        SphereCollider probe = probeObject.AddComponent<SphereCollider>();
        probe.isTrigger = true;
        probe.radius = epsilon;

        bool inside = Physics.ComputePenetration(
            probe,
            point,
            Quaternion.identity,
            targetCollider,
            targetCollider.transform.position,
            targetCollider.transform.rotation,
            out _,
            out float penetrationDistance);

        if (Application.isPlaying)
        {
            Object.Destroy(probeObject);
        }
        else
        {
            Object.DestroyImmediate(probeObject);
        }

        return inside && penetrationDistance > 0f;
    }

    private void ScanObjectToGridSurfaceVoxelization(
        Mesh meshTarget,
        Vector3[] vertices,
        Color[] vertexColors,
        bool hasVertexColors,
        Color fallbackColor,
        Vector3 gridCenter,
        Matrix4x4 blobToWorld,
        Matrix4x4 blobWorldToLocal,
        Matrix4x4 targetWorldToLocal,
        Matrix4x4 targetLocalToWorld,
        Vector3 scanOffset)
    {
        int maxX = size.x;
        int maxY = size.y;
        int maxZ = size.z;

        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                for (int z = 0; z < maxZ; z++)
                {
                    scanVoxels[x, y, z] = 0f;
                    scanColors[x, y, z] = fallbackColor;
                }
            }
        }

        bool[,,] surface = new bool[maxX, maxY, maxZ];
        int[] triangles = meshTarget.triangles;
        float maxSurfaceDistance = voxelSize * Mathf.Clamp(scanSurfaceThickness, 0.05f, 1f);
        float maxSurfaceDistanceSq = maxSurfaceDistance * maxSurfaceDistance;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0Local = vertices[triangles[i]];
            Vector3 v1Local = vertices[triangles[i + 1]];
            Vector3 v2Local = vertices[triangles[i + 2]];

            Vector3 v0World = targetLocalToWorld.MultiplyPoint3x4(v0Local);
            Vector3 v1World = targetLocalToWorld.MultiplyPoint3x4(v1Local);
            Vector3 v2World = targetLocalToWorld.MultiplyPoint3x4(v2Local);

            Vector3 v0 = blobWorldToLocal.MultiplyPoint3x4(v0World);
            Vector3 v1 = blobWorldToLocal.MultiplyPoint3x4(v1World);
            Vector3 v2 = blobWorldToLocal.MultiplyPoint3x4(v2World);

            Vector3 minLocal = Vector3.Min(v0, Vector3.Min(v1, v2));
            Vector3 maxLocal = Vector3.Max(v0, Vector3.Max(v1, v2));

            Vector3 minVoxel = minLocal / voxelSize + gridCenter;
            Vector3 maxVoxel = maxLocal / voxelSize + gridCenter;

            int startX = Mathf.Clamp(Mathf.FloorToInt(minVoxel.x) - 1, 0, maxX - 1);
            int startY = Mathf.Clamp(Mathf.FloorToInt(minVoxel.y) - 1, 0, maxY - 1);
            int startZ = Mathf.Clamp(Mathf.FloorToInt(minVoxel.z) - 1, 0, maxZ - 1);
            int endX = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.x) + 1, 0, maxX - 1);
            int endY = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.y) + 1, 0, maxY - 1);
            int endZ = Mathf.Clamp(Mathf.CeilToInt(maxVoxel.z) + 1, 0, maxZ - 1);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    for (int z = startZ; z <= endZ; z++)
                    {
                        Vector3 voxelCenterLocal = (new Vector3(x, y, z) - gridCenter) * voxelSize;
                        float distSq = PointTriangleDistanceSquared(voxelCenterLocal, v0, v1, v2);
                        if (distSq <= maxSurfaceDistanceSq)
                        {
                            surface[x, y, z] = true;
                        }
                    }
                }
            }
        }

        bool[,,] blocked = new bool[maxX, maxY, maxZ];
        int seal = Mathf.Max(0, scanSurfaceSealVoxels);
        if (seal == 0)
        {
            for (int x = 0; x < maxX; x++)
            {
                for (int y = 0; y < maxY; y++)
                {
                    for (int z = 0; z < maxZ; z++)
                    {
                        blocked[x, y, z] = surface[x, y, z];
                    }
                }
            }
        }
        else
        {
            for (int x = 0; x < maxX; x++)
            {
                for (int y = 0; y < maxY; y++)
                {
                    for (int z = 0; z < maxZ; z++)
                    {
                        if (!surface[x, y, z])
                        {
                            continue;
                        }

                        int startX = Mathf.Max(0, x - seal);
                        int endX = Mathf.Min(maxX - 1, x + seal);
                        int startY = Mathf.Max(0, y - seal);
                        int endY = Mathf.Min(maxY - 1, y + seal);
                        int startZ = Mathf.Max(0, z - seal);
                        int endZ = Mathf.Min(maxZ - 1, z + seal);
                        for (int nx = startX; nx <= endX; nx++)
                        {
                            for (int ny = startY; ny <= endY; ny++)
                            {
                                for (int nz = startZ; nz <= endZ; nz++)
                                {
                                    blocked[nx, ny, nz] = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        bool[,,] outside = new bool[maxX, maxY, maxZ];
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                EnqueueIfOutside(queue, outside, blocked, x, y, 0);
                EnqueueIfOutside(queue, outside, blocked, x, y, maxZ - 1);
            }
        }
        for (int x = 0; x < maxX; x++)
        {
            for (int z = 0; z < maxZ; z++)
            {
                EnqueueIfOutside(queue, outside, blocked, x, 0, z);
                EnqueueIfOutside(queue, outside, blocked, x, maxY - 1, z);
            }
        }
        for (int y = 0; y < maxY; y++)
        {
            for (int z = 0; z < maxZ; z++)
            {
                EnqueueIfOutside(queue, outside, blocked, 0, y, z);
                EnqueueIfOutside(queue, outside, blocked, maxX - 1, y, z);
            }
        }

        while (queue.Count > 0)
        {
            Vector3Int p = queue.Dequeue();
            int x = p.x;
            int y = p.y;
            int z = p.z;
            EnqueueIfOutside(queue, outside, blocked, x - 1, y, z);
            EnqueueIfOutside(queue, outside, blocked, x + 1, y, z);
            EnqueueIfOutside(queue, outside, blocked, x, y - 1, z);
            EnqueueIfOutside(queue, outside, blocked, x, y + 1, z);
            EnqueueIfOutside(queue, outside, blocked, x, y, z - 1);
            EnqueueIfOutside(queue, outside, blocked, x, y, z + 1);
        }

        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                for (int z = 0; z < maxZ; z++)
                {
                    if (outside[x, y, z] && !blocked[x, y, z])
                    {
                        continue;
                    }

                    scanVoxels[x, y, z] = 1f;
                    Vector3 local = new Vector3(x, y, z) - gridCenter;
                    Vector3 world = blobToWorld.MultiplyPoint3x4(local * voxelSize);
                    Vector3 scanWorld = world + scanOffset;
                    Vector3 scanLocal = targetWorldToLocal.MultiplyPoint3x4(scanWorld);
                    scanColors[x, y, z] = SampleScanColorLocal(
                        scanLocal,
                        vertices,
                        vertexColors,
                        hasVertexColors,
                        fallbackColor
                    );
                }
            }
        }
    }

    private static void EnqueueIfOutside(
        Queue<Vector3Int> queue,
        bool[,,] outside,
        bool[,,] surface,
        int x,
        int y,
        int z)
    {
        int maxX = outside.GetLength(0);
        int maxY = outside.GetLength(1);
        int maxZ = outside.GetLength(2);
        if (x < 0 || y < 0 || z < 0 || x >= maxX || y >= maxY || z >= maxZ)
        {
            return;
        }

        if (outside[x, y, z] || surface[x, y, z])
        {
            return;
        }

        outside[x, y, z] = true;
        queue.Enqueue(new Vector3Int(x, y, z));
    }

    private readonly struct VertexKey
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public VertexKey(Vector3 position, float precision)
        {
            x = Mathf.RoundToInt(position.x * precision);
            y = Mathf.RoundToInt(position.y * precision);
            z = Mathf.RoundToInt(position.z * precision);
        }
    }
}
