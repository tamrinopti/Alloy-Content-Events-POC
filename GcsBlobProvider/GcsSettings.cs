using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GcsBlobProvider.GcsBlobProvider
{
    public class GcsSettings
    {
        public string BucketName { get; set; }
        public string ServiceAccountKeyPath { get; set; }
        public int SignedUrlDurationMinutes { get; set; } = 60;
    }
}
