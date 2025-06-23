namespace Il2CppDumper
{
    class Il2CppConstants
    {
        /*
         * Field Attributes (21.1.5).
         */
        public const int FIELD_ATTRIBUTE_FIELD_ACCESS_MASK = 0x0007;
        public const int FIELD_ATTRIBUTE_COMPILER_CONTROLLED = 0x0000;
        public const int FIELD_ATTRIBUTE_PRIVATE = 0x0001;
        public const int FIELD_ATTRIBUTE_FAM_AND_ASSEM = 0x0002;
        public const int FIELD_ATTRIBUTE_ASSEMBLY = 0x0003;
        public const int FIELD_ATTRIBUTE_FAMILY = 0x0004;
        public const int FIELD_ATTRIBUTE_FAM_OR_ASSEM = 0x0005;
        public const int FIELD_ATTRIBUTE_PUBLIC = 0x0006;

        public const int FIELD_ATTRIBUTE_STATIC = 0x0010;
        public const int FIELD_ATTRIBUTE_INIT_ONLY = 0x0020;
        public const int FIELD_ATTRIBUTE_LITERAL = 0x0040;

        /*
         * Method Attributes (22.1.9)
         */
        public const int METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK = 0x0007;
        public const int METHOD_ATTRIBUTE_COMPILER_CONTROLLED = 0x0000;
        public const int METHOD_ATTRIBUTE_PRIVATE = 0x0001;
        public const int METHOD_ATTRIBUTE_FAM_AND_ASSEM = 0x0002;
        public const int METHOD_ATTRIBUTE_ASSEM = 0x0003;
        public const int METHOD_ATTRIBUTE_FAMILY = 0x0004;
        public const int METHOD_ATTRIBUTE_FAM_OR_ASSEM = 0x0005;
        public const int METHOD_ATTRIBUTE_PUBLIC = 0x0006;

        public const int METHOD_ATTRIBUTE_STATIC = 0x0010;
        public const int METHOD_ATTRIBUTE_FINAL = 0x0020;
        public const int METHOD_ATTRIBUTE_VIRTUAL = 0x0040;

        public const int METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK = 0x0100;
        public const int METHOD_ATTRIBUTE_REUSE_SLOT = 0x0000;
        public const int METHOD_ATTRIBUTE_NEW_SLOT = 0x0100;

        public const int METHOD_ATTRIBUTE_ABSTRACT = 0x0400;

        public const int METHOD_ATTRIBUTE_PINVOKE_IMPL = 0x2000;

        /*
        * Type Attributes (21.1.13).
        */
        public const int TYPE_ATTRIBUTE_VISIBILITY_MASK = 0x00000007;
        public const int TYPE_ATTRIBUTE_NOT_PUBLIC = 0x00000000;
        public const int TYPE_ATTRIBUTE_PUBLIC = 0x00000001;
        public const int TYPE_ATTRIBUTE_NESTED_PUBLIC = 0x00000002;
        public const int TYPE_ATTRIBUTE_NESTED_PRIVATE = 0x00000003;
        public const int TYPE_ATTRIBUTE_NESTED_FAMILY = 0x00000004;
        public const int TYPE_ATTRIBUTE_NESTED_ASSEMBLY = 0x00000005;
        public const int TYPE_ATTRIBUTE_NESTED_FAM_AND_ASSEM = 0x00000006;
        public const int TYPE_ATTRIBUTE_NESTED_FAM_OR_ASSEM = 0x00000007;


        public const int TYPE_ATTRIBUTE_INTERFACE = 0x00000020;

        public const int TYPE_ATTRIBUTE_ABSTRACT = 0x00000080;
        public const int TYPE_ATTRIBUTE_SEALED = 0x00000100;

        public const int TYPE_ATTRIBUTE_SERIALIZABLE = 0x00002000;

        /*
        * Flags for Params (22.1.12)
        */
        public const int PARAM_ATTRIBUTE_IN = 0x0001;
        public const int PARAM_ATTRIBUTE_OUT = 0x0002;
        public const int PARAM_ATTRIBUTE_OPTIONAL = 0x0010;

        /*
        * ELF Machine Types (e_machine)
        */
        public const ushort EM_NONE = 0; // No machine
        public const ushort EM_M32 = 1; // AT&T WE 32100
        public const ushort EM_SPARC = 2; // SPARC
        public const ushort EM_386 = 3; // Intel 80386
        public const ushort EM_68K = 4; // Motorola 68000
        public const ushort EM_88K = 5; // Motorola 88000
        // Далее много других, добавим только нужные
        public const ushort EM_860 = 7; // Intel 80860
        public const ushort EM_MIPS = 8; // MIPS R3000
        // ...
        public const ushort EM_ARM = 40; // ARM
        public const ushort EM_X86_64 = 62; // AMD x86-64
        public const ushort EM_AARCH64 = 183; // ARM64
        // Добавьте другие по мере необходимости

        /*
        * Mach-O CPU Types
        */
        public const int CPU_ARCH_MASK = unchecked((int)0xff000000); // Mask for architecture bits
        public const int CPU_ARCH_ABI64 = 0x01000000; // 64-bit ABI

        public const int CPU_TYPE_X86 = 7;
        public const int CPU_TYPE_X86_64 = CPU_TYPE_X86 | CPU_ARCH_ABI64;
        public const int CPU_TYPE_ARM = 12;
        public const int CPU_TYPE_ARM64 = CPU_TYPE_ARM | CPU_ARCH_ABI64;
        // Добавьте другие по мере необходимости (PPC, etc.)
    }
}
