using System.Collections;
using System.Collections.Generic;
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
    public float scanlinePreviewScale = 1f;

    [Header("Scanline Input")]
    public float scanlineMouseScaleSensitivity = 0.002f;
    public float scanlineScrollScaleSensitivity = 0.001f;
    public float scanlinePauseBudgetSeconds = 5f;

    [Header("Scan Grid")]
    public float scanDistanceMax = 2f;
    public GameObject scanTarget;

    private float[,,] voxels;
    private Color[,,] colors;
    private float[,,] scanVoxels;
    private Color[,,] scanColors;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private Coroutine scanlineRoutine;
    private int scanlineCurrentLayer = -1;
    private Vector3Int lastValidatedSize;
    private float lastValidatedVoxelSize = -1f;
    private float lastValidatedIsoLevel = -1f;
    private InputSystem_Actions inputActions;
    private bool isRightMouseHeld;
    private bool pauseHeld;
    private float pauseBudgetRemaining;

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
        inputActions.Player.Jump.performed += OnJumpPerformed;
        inputActions.Player.Jump.canceled += OnJumpCanceled;
        inputActions.Player.Interact.started += OnToggleShape;
        inputActions.UI.RightClick.performed += OnRightClickPerformed;
        inputActions.UI.RightClick.canceled += OnRightClickCanceled;
    }

    private void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Jump.performed -= OnJumpPerformed;
        inputActions.Player.Jump.canceled -= OnJumpCanceled;
        inputActions.Player.Interact.started -= OnToggleShape;
        inputActions.UI.RightClick.performed -= OnRightClickPerformed;
        inputActions.UI.RightClick.canceled -= OnRightClickCanceled;
        inputActions.Disable();
    }

    private void Update()
    {
        SyncRightMouseHeld();
        HandleScanlineInput();
        UpdateScanlinePreviewLive();
    }

    private void SyncRightMouseHeld()
    {
        if (Mouse.current == null)
        {
            return;
        }

        isRightMouseHeld = Mouse.current.rightButton.isPressed;
    }

    private void HandleScanlineInput()
    {
        if (!isRightMouseHeld || inputActions == null)
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
        scanlinePreviewScale = Mathf.Max(0.01f, scanlinePreviewScale);
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
                    colors[x, y, z] = new Color(Random.value, Random.value, Random.value, 1f);
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
                        colors[x, y, z] = new Color(Random.value, Random.value, Random.value, 1f);
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

        pauseBudgetRemaining = scanlinePauseBudgetSeconds;
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

        MeshRenderer rendererTarget = target.GetComponent<MeshRenderer>();
        Color fallbackColor = rendererTarget != null && rendererTarget.sharedMaterial != null
            ? rendererTarget.sharedMaterial.color
            : Color.white;

        Color[] vertexColors = meshTarget != null ? meshTarget.colors : null;
        Vector3[] vertices = meshTarget != null ? meshTarget.vertices : System.Array.Empty<Vector3>();
        bool hasVertexColors = vertexColors != null && vertexColors.Length == vertices.Length && vertices.Length > 0;

        Collider scanCollider = target.GetComponent<Collider>();
        bool createdCollider = false;
        if (scanCollider == null)
        {
            if (meshTarget == null)
            {
                return;
            }

            MeshCollider meshCollider = target.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshTarget;
            meshCollider.convex = true;
            scanCollider = meshCollider;
            createdCollider = true;
        }

        GameObject probeObject = null;
        SphereCollider probeCollider = null;
        probeObject = new GameObject("ScanProbe");
        probeObject.hideFlags = HideFlags.HideAndDontSave;
        probeCollider = probeObject.AddComponent<SphereCollider>();
        probeCollider.radius = Mathf.Max(0.0001f, voxelSize * 0.001f);
        Vector3 gridCenter = GetGridCenter();
        Vector3 scanOffset = target.transform.position - transform.position;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    Vector3 local = new Vector3(x, y, z) - gridCenter;
                    Vector3 world = transform.TransformPoint(local * voxelSize);
                    Vector3 scanWorld = world + scanOffset;
                    probeObject.transform.position = scanWorld;
                    float signedDistance = GetSignedDistance(scanWorld, scanCollider, probeCollider);
                    float value = Mathf.Clamp01(isoLevel - (signedDistance / (2f * scanDistanceMax)));
                    scanVoxels[x, y, z] = value;
                    scanColors[x, y, z] = SampleScanColor(scanWorld, target.transform, vertices, vertexColors, hasVertexColors, fallbackColor);
                }
            }
        }

        if (createdCollider)
        {
            Destroy(scanCollider);
        }
        DestroyImmediate(probeObject);
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

        int steps = Mathf.Max(1, rotationSteps);
        float bestScore = float.NegativeInfinity;
        bestRotationDegrees = 0f;

        for (int step = 0; step < steps; step++)
        {
            float angle = 360f * step / steps;
            float score = ComputeSimilarity(angle, includeColors);
            if (score > bestScore)
            {
                bestScore = score;
                bestRotationDegrees = angle;
            }
        }

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
        var vertexNormals = new List<Vector3>(2048);
        var vertexNormalCounts = new List<int>(2048);
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
                        edgeVertex[i] = Vector3.Lerp(p0, p1, offset) * voxelSize;
                        Color c0 = GetColorSafe(x + VertexOffset[v0, 0], y + VertexOffset[v0, 1], z + VertexOffset[v0, 2]);
                        Color c1 = GetColorSafe(x + VertexOffset[v1, 0], y + VertexOffset[v1, 1], z + VertexOffset[v1, 2]);
                        edgeColor[i] = Color.Lerp(c0, c1, offset);
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
        for (int i = 0; i < vertexNormals.Count; i++)
        {
            Vector3 n = vertexNormals[i];
            vertexNormals[i] = n.sqrMagnitude > 0.000001f ? n.normalized : Vector3.up;
        }

        mesh.SetColors(vertexColors);
        mesh.SetNormals(vertexNormals);
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

            int ncount = normalCounts[index];
            Vector3 normal = ComputeNormal(position);
            normals[index] = (normals[index] * ncount + normal) / (ncount + 1);
            normalCounts[index] = ncount + 1;
            return index;
        }

        int newIndex = vertices.Count;
        vertices.Add(position);
        colors.Add(color);
        colorCounts.Add(1);
        normals.Add(ComputeNormal(position));
        normalCounts.Add(1);
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
        float delay = 1f / Mathf.Max(0.01f, scanlineLayersPerSecond);

        for (int y = 0; y < size.y; y++)
        {
            Color layerColor = new Color(Random.value, Random.value, Random.value, 1f);
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
        HideScanlinePreviews();
    }

    private IEnumerator WaitForScanlineDelay(float delay)
    {
        float remaining = delay;
        while (remaining > 0f)
        {
            if (pauseHeld && pauseBudgetRemaining > 0f)
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
        float scale = scanlinePreviewScale;
        active.localScale = new Vector3(
            halfExtents.x * 2f * voxelSize * scale,
            voxelSize * scale,
            halfExtents.y * 2f * voxelSize * scale
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

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (scanlineRoutine == null)
        {
            StartScanline();
            return;
        }

        pauseHeld = true;
    }

    private void OnJumpCanceled(InputAction.CallbackContext context)
    {
        pauseHeld = false;
    }

    private void OnToggleShape(InputAction.CallbackContext context)
    {
        scanlineShape = scanlineShape == ScanlineShape.Cube ? ScanlineShape.Cylinder : ScanlineShape.Cube;
    }

    private void OnRightClickPerformed(InputAction.CallbackContext context)
    {
        isRightMouseHeld = true;
    }

    private void OnRightClickCanceled(InputAction.CallbackContext context)
    {
        isRightMouseHeld = false;
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
        if (colors == null || !InBounds(x, y, z))
        {
            return Color.black;
        }

        return colors[x, y, z];
    }

    private Vector3 ComputeNormal(Vector3 position)
    {
        float safeVoxelSize = Mathf.Max(0.0001f, voxelSize);
        Vector3 p = position / safeVoxelSize + GetGridCenter();
        const float epsilon = 0.5f;
        float dx = SampleField(p.x + epsilon, p.y, p.z) - SampleField(p.x - epsilon, p.y, p.z);
        float dy = SampleField(p.x, p.y + epsilon, p.z) - SampleField(p.x, p.y - epsilon, p.z);
        float dz = SampleField(p.x, p.y, p.z + epsilon) - SampleField(p.x, p.y, p.z - epsilon);
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
        Vector3 center = GetGridCenter();
        float radians = rotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        float total = 0f;
        float colorTotal = 0f;
        int count = 0;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    Vector3 p = new Vector3(x, y, z) - center;
                    float rx = p.x * cos - p.z * sin;
                    float rz = p.x * sin + p.z * cos;
                    Vector3 rotated = new Vector3(rx, p.y, rz) + center;

                    float scanValue = SampleScanField(rotated.x, rotated.y, rotated.z);
                    float diff = Mathf.Abs(voxels[x, y, z] - scanValue);
                    total += diff;

                    if (includeColors)
                    {
                        Color scanColor = SampleScanColor(rotated.x, rotated.y, rotated.z);
                        Color current = colors[x, y, z];
                        colorTotal += Mathf.Abs(current.r - scanColor.r) +
                                      Mathf.Abs(current.g - scanColor.g) +
                                      Mathf.Abs(current.b - scanColor.b);
                    }

                    count++;
                }
            }
        }

        if (count == 0)
        {
            return 0f;
        }

        float valueScore = 1f - (total / count);
        if (!includeColors)
        {
            return valueScore;
        }

        float colorScore = 1f - (colorTotal / (count * 3f));
        return (valueScore + colorScore) * 0.5f;
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

        Vector3 closest = collider.ClosestPoint(worldPoint);
        float distance = Vector3.Distance(worldPoint, closest);

        float penetrationDistance = 0f;
        bool penetrationInside = Physics.ComputePenetration(
                probe,
                probe.transform.position,
                probe.transform.rotation,
                collider,
                collider.transform.position,
                collider.transform.rotation,
                out _,
                out penetrationDistance);

        bool inside = penetrationInside || distance <= 0.0001f;
        if (inside)
        {
            float insideDistance = penetrationDistance > 0f ? penetrationDistance : Mathf.Max(distance, voxelSize * 0.5f);
            return -insideDistance;
        }

        return distance;
    }

    private Color SampleScanColor(
        Vector3 worldPoint,
        Transform targetTransform,
        Vector3[] vertices,
        Color[] vertexColors,
        bool hasVertexColors,
        Color fallback)
    {
        if (!hasVertexColors || vertices.Length == 0)
        {
            return fallback;
        }

        Vector3 local = targetTransform.InverseTransformPoint(worldPoint);
        float best = float.PositiveInfinity;
        int bestIndex = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float d = (vertices[i] - local).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestIndex = i;
            }
        }

        return vertexColors[bestIndex];
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
