using System;

namespace Ludots.Core.Presentation.Assets
{
    internal sealed class PrefabGroundingBatchContext
    {
        private float[] _xsCm;
        private float[] _ysCm;
        private float[] _heightsCm;
        private float[] _originXMeters;
        private float[] _originYMeters;
        private float[] _originZMeters;
        private float[] _directionX;
        private float[] _directionY;
        private float[] _directionZ;
        private float[] _hitWorldXCm;
        private float[] _hitWorldYCm;
        private float[] _hitHeightCm;
        private float[] _hitDistanceMeters;
        private float[] _hitNormalX;
        private float[] _hitNormalY;
        private float[] _hitNormalZ;
        private int[] _hitLayerIndex;
        private byte[] _hitMask;
        private int[] _requestIndices;
        private bool[] _processed;

        public PrefabGroundingBatchContext(int capacity = 32)
        {
            _xsCm = new float[capacity];
            _ysCm = new float[capacity];
            _heightsCm = new float[capacity];
            _originXMeters = new float[capacity];
            _originYMeters = new float[capacity];
            _originZMeters = new float[capacity];
            _directionX = new float[capacity];
            _directionY = new float[capacity];
            _directionZ = new float[capacity];
            _hitWorldXCm = new float[capacity];
            _hitWorldYCm = new float[capacity];
            _hitHeightCm = new float[capacity];
            _hitDistanceMeters = new float[capacity];
            _hitNormalX = new float[capacity];
            _hitNormalY = new float[capacity];
            _hitNormalZ = new float[capacity];
            _hitLayerIndex = new int[capacity];
            _hitMask = new byte[capacity];
            _requestIndices = new int[capacity];
            _processed = new bool[capacity];
        }

        public float[] XsCm => _xsCm;
        public float[] YsCm => _ysCm;
        public float[] HeightsCm => _heightsCm;
        public float[] OriginXMeters => _originXMeters;
        public float[] OriginYMeters => _originYMeters;
        public float[] OriginZMeters => _originZMeters;
        public float[] DirectionX => _directionX;
        public float[] DirectionY => _directionY;
        public float[] DirectionZ => _directionZ;
        public float[] HitWorldXCm => _hitWorldXCm;
        public float[] HitWorldYCm => _hitWorldYCm;
        public float[] HitHeightCm => _hitHeightCm;
        public float[] HitDistanceMeters => _hitDistanceMeters;
        public float[] HitNormalX => _hitNormalX;
        public float[] HitNormalY => _hitNormalY;
        public float[] HitNormalZ => _hitNormalZ;
        public int[] HitLayerIndex => _hitLayerIndex;
        public byte[] HitMask => _hitMask;
        public int[] RequestIndices => _requestIndices;
        public bool[] Processed => _processed;

        public void EnsureCapacity(int required)
        {
            if (required <= _xsCm.Length)
            {
                return;
            }

            int next = Math.Max(required, _xsCm.Length * 2);
            Array.Resize(ref _xsCm, next);
            Array.Resize(ref _ysCm, next);
            Array.Resize(ref _heightsCm, next);
            Array.Resize(ref _originXMeters, next);
            Array.Resize(ref _originYMeters, next);
            Array.Resize(ref _originZMeters, next);
            Array.Resize(ref _directionX, next);
            Array.Resize(ref _directionY, next);
            Array.Resize(ref _directionZ, next);
            Array.Resize(ref _hitWorldXCm, next);
            Array.Resize(ref _hitWorldYCm, next);
            Array.Resize(ref _hitHeightCm, next);
            Array.Resize(ref _hitDistanceMeters, next);
            Array.Resize(ref _hitNormalX, next);
            Array.Resize(ref _hitNormalY, next);
            Array.Resize(ref _hitNormalZ, next);
            Array.Resize(ref _hitLayerIndex, next);
            Array.Resize(ref _hitMask, next);
            Array.Resize(ref _requestIndices, next);
            Array.Resize(ref _processed, next);
        }
    }
}
